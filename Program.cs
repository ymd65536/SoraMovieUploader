using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Core;

using System.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

using DotNETSora.Models;

// --- Main Program (Top-Level Statements for .NET 8) ---

// 環境変数から値を取得
var Endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? string.Empty;
var ApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? string.Empty;
var UseApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_USE_API_KEY") ?? "0";

// APIバージョンは引き続き定数として扱う
const string ApiVersion = "preview";
const string outputFilename = "output.mp4";

if (string.IsNullOrEmpty(Endpoint))
{
    Console.WriteLine("エラー: 環境変数 AZURE_OPENAI_ENDPOINT が設定されていません。");
    Console.WriteLine("続行する前に、環境変数AZURE_OPENAI_ENDPOINTを設定してください。");
    return;
}

// トップレベルで async/await を直接使用可能
try
{
    if (File.Exists(outputFilename))
    {
        Console.WriteLine($"'{outputFilename}' が既に存在します。アップロード処理を開始します。");
        await UploadVideoAsync(outputFilename);
        return;
    }

    using var client = new HttpClient();

    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));


    if (string.Equals(UseApiKey, "0"))
    {
        var credential = new DefaultAzureCredential();
        var tokenRequestContext = new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" });
        var token = await credential.GetTokenAsync(tokenRequestContext);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }
    else
    {
        Console.WriteLine("⚠️ 警告: APIキー認証は推奨されていません。可能な限りEntra ID認証を使用してください。");
        client.DefaultRequestHeaders.Add("api-key", ApiKey);
    }

    // 1. Create a video generation job
    var createUrl = $"{Endpoint}/openai/v1/video/generations/jobs?api-version={ApiVersion}";
    var requestBody = new VideoGenerationRequest();
    requestBody.Model = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? requestBody.Model;

    var json = JsonSerializer.Serialize(requestBody);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    Console.WriteLine("--- 1. ジョブを作成中 ---");
    Console.WriteLine($"エンドポイント: {Endpoint}");
    var response = await client.PostAsync(createUrl, content);
    response.EnsureSuccessStatusCode();

    var responseJson = await response.Content.ReadAsStringAsync();
    JobStatusResponse? jobCreationResponse = JsonSerializer.Deserialize<JobStatusResponse>(responseJson);
    
    var jobId = jobCreationResponse?.Id;

    if (string.IsNullOrEmpty(jobId))
    {
        throw new Exception("ジョブIDがレスポンスから取得できませんでした。");
    }
    
    Console.WriteLine($"ジョブが作成されました: {jobId}");
    Console.WriteLine($"レスポンスJSON: {responseJson}");

    // 2. Poll for job status
    var statusUrl = $"{Endpoint}/openai/v1/video/generations/jobs/{jobId}?api-version={ApiVersion}";
    string status = string.Empty;
    JobStatusResponse? jobStatus = null;

    Console.WriteLine("\n--- 2. ステータスを確認中 (5秒ごとにポーリング) ---");
    while (status != "succeeded" && status != "failed" && status != "cancelled")
    {
        await Task.Delay(5000); // 5秒待機 (time.sleep(5)に相当)

        var statusResponse = await client.GetAsync(statusUrl);
        statusResponse.EnsureSuccessStatusCode();
        
    var statusJson = await statusResponse.Content.ReadAsStringAsync();
    // ポーリング応答を最新のステータスで上書き
    jobStatus = JsonSerializer.Deserialize<JobStatusResponse>(statusJson);

    status = jobStatus?.Status ?? string.Empty;
        Console.WriteLine($"ジョブステータス: {status}");
    }

    // 3. Retrieve generated video
    if (status == "succeeded")
    {
        Console.WriteLine("✅ ビデオ生成が成功しました。");
        
        var generations = jobStatus?.Generations;
        if (generations?.Count > 0)
        {
            var generationId = generations[0].Id;
            var videoUrl = $"{Endpoint}/openai/v1/video/generations/{generationId}/content/video?api-version={ApiVersion}";
            
            Console.WriteLine($"ビデオコンテンツをダウンロード中...");
            
            // ビデオのバイナリデータを取得
            var videoResponse = await client.GetAsync(videoUrl);
            videoResponse.EnsureSuccessStatusCode();
            
            var videoBytes = await videoResponse.Content.ReadAsByteArrayAsync();

            // ファイルに保存
            await File.WriteAllBytesAsync(outputFilename, videoBytes);
            
            Console.WriteLine($"生成されたビデオは「{outputFilename}」として保存されました。");

            // 4. Upload to YouTube
            Console.WriteLine("\n--- 4. YouTubeにアップロード中 ---");
            await UploadVideoAsync(outputFilename);
        }
        else
        {
            throw new Exception("ジョブ結果に生成されたビデオが見つかりませんでした。");
        }
    }
    else
    {
        throw new Exception($"ジョブは成功しませんでした。ステータス: {status}");
    }
}
catch (HttpRequestException ex)
{
    Console.WriteLine($"HTTPリクエストエラー: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"エラーが発生しました: {ex.Message}");
}

async Task UploadVideoAsync(string videoPath)
{
    // Prefer OAuth installed-app flow so the user can grant the correct YouTube scopes.
    // This will open a browser for consent and persist the token to a local FileDataStore.
    // If client_secrets.json is not found, fall back to Application Default Credentials (ADC).
    BaseClientService.Initializer initializer = new BaseClientService.Initializer()
    {
        ApplicationName = "SoraMovieUploader"
    };

    Google.Apis.Auth.OAuth2.UserCredential? userCredential = null;
    try
    {
        // Look for client_secrets.json in the working directory
        var credPath = Path.Combine(Directory.GetCurrentDirectory(), "client_secrets.json");
        if (File.Exists(credPath))
        {
            using var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read);
            // Request the youtube.upload scope
            string[] scopes = new[] { YouTubeService.Scope.YoutubeUpload };
            // Store credentials in a folder named "token.json" (FileDataStore)
            var fileDataStore = new Google.Apis.Util.Store.FileDataStore("SoraMovieUploader.TokenStore");
            userCredential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                scopes,
                "user",
                CancellationToken.None,
                fileDataStore
            );

            initializer.HttpClientInitializer = userCredential;
        }
        else
        {
            // Fallback to ADC: still try to create scoped credential if possible
            var adc = await GoogleCredential.GetApplicationDefaultAsync();
            if (adc.IsCreateScopedRequired)
            {
                adc = adc.CreateScoped(YouTubeService.Scope.YoutubeUpload);
            }
            initializer.HttpClientInitializer = adc;
            Console.WriteLine("⚠️ client_secrets.json not found. Falling back to Application Default Credentials. Ensure ADC has youtube.upload scope granted.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"認証の初期化に失敗しました: {ex.Message}");
        throw;
    }

    var youtubeService = new YouTubeService(initializer);

    var video = new Video();
    video.Snippet = new VideoSnippet();
    video.Snippet.Title = "Generated by Sora";
    video.Snippet.Description = "This video was generated by Azure OpenAI Sora model and uploaded via API.";
    video.Snippet.Tags = new string[] { "Sora", "OpenAI", "Azure" };
    video.Snippet.CategoryId = "28"; // Category for Science & Technology
    video.Status = new VideoStatus();
    video.Status.PrivacyStatus = "private"; // or "unlisted" or "public"

    using (var fileStream = new FileStream(videoPath, FileMode.Open))
    {
        var videosInsertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");
        videosInsertRequest.ProgressChanged += OnProgressChanged;
        videosInsertRequest.ResponseReceived += OnResponseReceived;

        Console.WriteLine("アップロードを開始します...");
        await videosInsertRequest.UploadAsync();
    }
}

void OnProgressChanged(IUploadProgress progress)
{
    switch (progress.Status)
    {
        case UploadStatus.Uploading:
            Console.WriteLine($"アップロード中: {progress.BytesSent} バイト送信済み");
            break;

        case UploadStatus.Failed:
            Console.WriteLine($"アップロード失敗: {progress.Exception}");
            break;
    }
}

void OnResponseReceived(Video video)
{
    Console.WriteLine($"✅ アップロード完了！");
    Console.WriteLine($"ビデオID: {video.Id}");
}