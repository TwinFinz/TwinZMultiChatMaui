using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using Google.Apis;
using Google.Apis.Upload;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.YouTube.v3.Data;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Discord.WebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TwitchLib.Api.Helix;
using TwitchLib.Client.Models;
using TwitchLib.Client.Events;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Communication.Interfaces;
using System.Net.Http;
using Google.Apis.Auth;
using Microsoft.Maui;
using Carbon.OAuth2;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Controls.PlatformConfiguration;
using CommunityToolkit.Maui.Core.Primitives;
using System.Xml;
using Google.Apis.Util;

#if ANDROID
using Android.Content;
#endif

namespace TwinZMultiChat.Utilities
{
    public class MyYoutubeAPI
    {
#pragma warning disable CA1822 // Mark members as static (I don't want them static)
        public readonly static string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinZMulitChat");
        public bool ChatListener = true;
        private static YouTubeService? _youtubeService;
        private static string applicationName = "";
        private static string streamMsgIntro = "";
        //private static readonly string youTubeRedirectUri = "http://localhost:8080/redirect/";
        private static bool isChatMessageListenerActive = false;
        private static Task? _chatMessageListenerTask;
        private static LiveBroadcast? curBroadcast;
        private static MainPage? UiForm;
        

        // Define the event
        public event EventHandler<ChatMessageEventArgs>? ChatMessageReceived;

        public MyYoutubeAPI(MainPage sender, string ApplicationName, string StreamMsgIntro)
        {
            
            applicationName = ApplicationName;
            streamMsgIntro = StreamMsgIntro;
            UiForm = sender;
        }

        // ------------------------------------------------------------------- //

 
        public Uri GenerateAuthorizationUrl()
        {
            // Load client secrets from the JSON file

            string filePath = Path.Combine(DataFolder, "client_secret.json"); 
            GoogleClientSecrets clientSecrets;
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                clientSecrets = GoogleClientSecrets.FromStream(stream);
            }

            // Create the authorization code flow
            var codeFlow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = clientSecrets.Secrets,
                Scopes = new[] { YouTubeService.Scope.Youtube }
            });

            // Generate the authorization URL
            var authorizationUrl = codeFlow.CreateAuthorizationCodeRequest("urn:ietf:wg:oauth:2.0:oob")
                                           .Build();

            return authorizationUrl;
        }

        public class GoogleOauthJson
        {
            [JsonProperty("installed")]
            public Installed? Installed { get; set; }
        }
        public class Installed
        {
            [JsonProperty("client_id")]
            public string ClientId { get; set; } = string.Empty;

            [JsonProperty("project_id")]
            public string ProjectId { get; set; } = string.Empty;

            [JsonProperty("auth_uri")]
            public string AuthUri { get; set; } = string.Empty;

            [JsonProperty("token_uri")]
            public string TokenUri { get; set; } = string.Empty;

            [JsonProperty("auth_provider_x509_cert_url")]
            public string AuthProviderX509CertUrl { get; set; } = string.Empty;
        }

        // ------------------------------------------------------------------- //

        public async Task ConnectAsync()
        {
            try
            {
                if (!Directory.Exists(DataFolder))
                {
                    Directory.CreateDirectory(DataFolder);
                }
                string filePath = Path.Combine(DataFolder, "client_secret.json");
                if (!File.Exists(filePath))
                {
                    await UiForm!.MessageBoxWithOK("Warning!", "client_secret.json NOT found. Please select the file downloaded from console.google.com.", "OK");
                    FileResult? fileResult = await FilePicker.PickAsync(new PickOptions
                    {
                        FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                        {
                            { DevicePlatform.iOS, new[] { "public.json" } },
                            { DevicePlatform.Android, new[] { "application/json" } },
                            { DevicePlatform.WinUI, new[] { ".json" } }
                        })
                    });

                    if (fileResult != null)
                    {
                        string newFilePath = Path.Combine(DataFolder, "client_secret.json");
                        if (File.Exists(newFilePath))
                        {
                            File.Delete(newFilePath);
                        }
                        File.Copy(fileResult.FullPath, newFilePath);
                        filePath = newFilePath;
                    }
                }

                UserCredential credential;
                using (FileStream stream = new(filePath, FileMode.Open, FileAccess.Read)!)
                {
                    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        new[] { YouTubeService.Scope.Youtube },
                        applicationName,
                        CancellationToken.None,
                        new FileDataStore(Assembly.GetExecutingAssembly().GetName().Name)
                    );
                }

                BaseClientService.Initializer initializer = new()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = Assembly.GetExecutingAssembly().GetName().Name
                };

                _youtubeService = new YouTubeService(initializer)!;
                curBroadcast = GetLiveBroadcast(_youtubeService!)!;

                _chatMessageListenerTask = Task.Run(StartChatMessageListener);
            }
            catch (Exception ex)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"An error occurred while connecting: {ex.Message}", "OK");
                throw new Exception(ex.Message);
            }
        } // does not work on android 

        public async Task ConnectAsyncAndroid()
        {
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                try
                {
                    if (!Directory.Exists(DataFolder))
                    {
                        Directory.CreateDirectory(DataFolder);
                    }
                    string filePath = Path.Combine(DataFolder, "client_secret.json");
                    if (!File.Exists(filePath))
                    {
                        await UiForm!.MessageBoxWithOK("Warning!", "client_secret.json NOT found. Please select the file downloaded from console.google.com.", "OK");
                        FileResult? fileResult = await FilePicker.PickAsync(new PickOptions
                        {
                            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                            {
                                { DevicePlatform.iOS, new[] { "public.json" } },
                                { DevicePlatform.Android, new[] { "application/json" } },
                                { DevicePlatform.WinUI, new[] { ".json" } }
                            })
                        });

                        if (fileResult != null)
                        {
                            File.Copy(fileResult.FullPath, filePath);
                        }
                    }

                    GoogleCredential credential;
                    using (FileStream stream = new(filePath, FileMode.Open, FileAccess.Read)!)
                    {
                        credential = GoogleCredential.FromStream(stream)
                            .CreateScoped(new[] { YouTubeService.Scope.Youtube });
                    }

                    BaseClientService.Initializer initializer = new()
                    {
                        HttpClientInitializer = credential,
                        ApplicationName = Assembly.GetExecutingAssembly().GetName().Name
                    };

                    _youtubeService = new YouTubeService(initializer)!;
                    curBroadcast = GetLiveBroadcast(_youtubeService!)!;

                    _chatMessageListenerTask = Task.Run(StartChatMessageListener);
                }
                catch (Exception ex)
                {
                    await UiForm!.MessageBoxWithOK("Warning!", $"An error occurred while connecting: {ex.Message}", "OK");
                    throw new Exception(ex.Message);
                }
            }
        } // Not Working on android

        public async Task ConnectAsyncCodeFlow()
        {
            try
            {
                string filePath = Path.Combine(DataFolder, "client_secret.json");
                if (!File.Exists(filePath))
                {
                    await UiForm!.MessageBoxWithOK("Warning!", "client_secret.json NOT found. Please select the file downloaded from console.google.com.", "OK");
                    FileResult? fileResult = await FilePicker.PickAsync(new PickOptions
                    {
                        FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.json" } },
                    { DevicePlatform.Android, new[] { "application/json" } },
                    { DevicePlatform.WinUI, new[] { ".json" } }
                })
                    });

                    if (fileResult != null)
                    {
                        string newFilePath = Path.Combine(DataFolder, "client_secret.json");
                        File.Copy(fileResult.FullPath, newFilePath);
                        filePath = fileResult.FullPath;
                    }
                }

                UserCredential credential;
                using (FileStream stream = new(filePath, FileMode.Open, FileAccess.Read)!)
                {
                    GoogleClientSecrets clientSecrets = GoogleClientSecrets.FromStream(stream);
                    var codeFlow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                    {
                        ClientSecrets = clientSecrets.Secrets,
                        Scopes = new[] { YouTubeService.Scope.Youtube }
                    });

                    credential = await new AuthorizationCodeInstalledApp(codeFlow, new LocalServerCodeReceiver()).AuthorizeAsync(
                        "user", CancellationToken.None
                    );
                }

                BaseClientService.Initializer initializer = new()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = Assembly.GetExecutingAssembly().GetName().Name
                };

                _youtubeService = new YouTubeService(initializer)!;
                curBroadcast = GetLiveBroadcast(_youtubeService!)!;

                _chatMessageListenerTask = Task.Run(StartChatMessageListener);
            }
            catch (Exception ex)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"An error occurred while connecting: {ex.Message}", "OK");
                throw new Exception(ex.Message);
            }
        }  // does not work on android

        public Task DisconnectAsync()
        {
            if (_youtubeService != null)
            {
                isChatMessageListenerActive = false;
                // Wait for the chat message listener task to complete (if it's still running)
                if (_chatMessageListenerTask != null)
                {
                    _chatMessageListenerTask.GetAwaiter().GetResult();
                    _chatMessageListenerTask = null;
                }
                _youtubeService!.Dispose();
            }
            return Task.CompletedTask;
        }

        // Method to raise the ChatMessageReceived event
        protected virtual void OnChatMessageReceived(ChatMessageEventArgs e)
        {
            ChatMessageReceived?.Invoke(this, e);
        }

        private async Task StartChatMessageListener()
        {
            string liveStreamId = await GetActiveLiveStreamId()!;
            if (string.IsNullOrEmpty(liveStreamId))
            {
                await UiForm!.MessageBoxWithOK("Warning!", "No active live streams found.", "OK");
                return;
            }

            string liveChatId = await GetLiveChatIdForCurrentStream(liveStreamId);
            if (string.IsNullOrEmpty(liveChatId))
            {
                await UiForm!.MessageBoxWithOK("Warning!", "No live chat ID found.", "OK");
                return;
            }

            HashSet<string> processedMessageIds = new (); // Track processed message IDs
            isChatMessageListenerActive = true;
            LiveChatMessageListResponse? liveChatMessages = null;
            IEnumerable<LiveChatMessage?> newMessages;
            while (isChatMessageListenerActive)
            {
                if (liveChatMessages == null || !string.IsNullOrEmpty(liveChatMessages.NextPageToken))
                {
                    liveChatMessages = await GetLiveChatMessages(liveChatId, liveChatMessages?.NextPageToken!);
                    if (liveChatMessages == null)
                    {
                        // Error occurred while fetching messages, break the loop
                        break;
                    }
                }

                newMessages = liveChatMessages.Items;
                foreach (LiveChatMessage? message in newMessages)
                {
                    if (message != null && !processedMessageIds.Contains(message.Id) && !message.Snippet.DisplayMessage.StartsWith(streamMsgIntro))
                    {
                        // Create an event argument object with the message data
                        ChatMessageEventArgs eventArgs = new (message.AuthorDetails.DisplayName, message.Snippet.DisplayMessage);
                        processedMessageIds.Add(message.Id); // Add message ID to processed collection
                        OnChatMessageReceived(eventArgs);    // Raise the event with the message data
                    }
                }

                await Task.Delay(10 * 1000);
            }
        }

        public async Task<SearchListResponse> SearchVideos(string query, int maxResults = 20)
        {
            SearchResource.ListRequest searchListRequest = _youtubeService!.Search.List("snippet")!;
            searchListRequest.Q = query;
            searchListRequest.MaxResults = maxResults;
            return await searchListRequest.ExecuteAsync()!;
        }

        public async Task<ChannelListResponse> GetChannel(string channelId)
        {
            ChannelsResource.ListRequest channelListRequest = _youtubeService!.Channels.List("snippet,contentDetails,statistics")!;
            channelListRequest.Id = channelId;
            return await channelListRequest.ExecuteAsync()!;
        }

        public async Task<VideoListResponse> GetVideo(string videoId)
        {
            VideosResource.ListRequest videoListRequest = _youtubeService!.Videos.List("snippet,contentDetails,statistics")!;
            videoListRequest.Id = videoId;
            return await videoListRequest.ExecuteAsync()!;
        }

        public async Task<PlaylistListResponse> GetPlaylist(string playlistId)
        {
            PlaylistsResource.ListRequest playlistListRequest = _youtubeService!.Playlists.List("snippet,contentDetails")!;
            playlistListRequest.Id = playlistId;
            return await playlistListRequest.ExecuteAsync()!;
        }

        public async Task<CommentThreadListResponse> GetVideoComments(string videoId, int maxResults = 20)
        {
            CommentThreadsResource.ListRequest commentThreadListRequest = _youtubeService!.CommentThreads.List("snippet")!;
            commentThreadListRequest.VideoId = videoId;
            commentThreadListRequest.MaxResults = maxResults;
            return await commentThreadListRequest.ExecuteAsync()!;
        }

        public async Task<PlaylistItemListResponse> GetPlaylistItems(string playlistId, int maxResults = 20)
        {
            PlaylistItemsResource.ListRequest playlistItemListRequest = _youtubeService!.PlaylistItems.List("snippet")!;
            playlistItemListRequest.PlaylistId = playlistId;
            playlistItemListRequest.MaxResults = maxResults;
            return await playlistItemListRequest.ExecuteAsync()!;
        }

        public async Task<SubscriptionListResponse> GetChannelSubscriptions(string channelId, int maxResults = 20)
        {
            SubscriptionsResource.ListRequest subscriptionListRequest = _youtubeService!.Subscriptions.List("snippet")!;
            subscriptionListRequest.ChannelId = channelId;
            subscriptionListRequest.MaxResults = maxResults;
            return await subscriptionListRequest.ExecuteAsync()!;
        }
           
        public async Task<ActivityListResponse> GetChannelActivities(string channelId, int maxResults = 20)
        {
            ActivitiesResource.ListRequest activityListRequest = _youtubeService!.Activities.List("snippet,contentDetails")!;
            activityListRequest.ChannelId = channelId;
            activityListRequest.MaxResults = maxResults;
            return await activityListRequest.ExecuteAsync()!;
        }

        public async Task<LiveBroadcastListResponse> GetLiveBroadcasts(LiveBroadcastsResource.ListRequest.BroadcastStatusEnum broadcastStatus, int maxResults = 20)
        {
            LiveBroadcastsResource.ListRequest liveBroadcastListRequest = _youtubeService!.LiveBroadcasts.List("snippet,contentDetails,status")!;
            liveBroadcastListRequest.BroadcastStatus = broadcastStatus;
            liveBroadcastListRequest.MaxResults = maxResults;
            return await liveBroadcastListRequest.ExecuteAsync()!;
        }

        public async Task<LiveStreamListResponse> GetLiveStreams(string part, int maxResults = 20)
        {
            LiveStreamsResource.ListRequest liveStreamListRequest = _youtubeService!.LiveStreams.List(part)!;
            liveStreamListRequest.MaxResults = maxResults;
            return await liveStreamListRequest.ExecuteAsync()!;
        }

        public async Task<LiveChatMessageListResponse> GetLiveChatMessages(string liveChatId, string pageToken = "", int maxResults = 6)
        {
                LiveChatMessagesResource.ListRequest liveChatMessagesListRequest = _youtubeService!.LiveChatMessages.List(liveChatId, "snippet,authorDetails")!;
                liveChatMessagesListRequest.PageToken = pageToken;
                liveChatMessagesListRequest.MaxResults = maxResults;
                return await liveChatMessagesListRequest.ExecuteAsync();
        }

        public Task<TimeSpan?> GetCurrentBroadcastLength()
        {
            if (_youtubeService == null || curBroadcast == null)
            {
                throw new InvalidOperationException("YouTube service or current broadcast is not available.");
            }
            DateTimeOffset? scheduledStartTime = curBroadcast.Snippet.ScheduledStartTimeDateTimeOffset;
            DateTimeOffset? currentTime = DateTimeOffset.UtcNow;
            TimeSpan? broadcastLength = (currentTime! - scheduledStartTime!);

            return Task.FromResult(broadcastLength);
        }

        public Task<string> GetLiveStreamStatus()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast.Status.LifeCycleStatus);
            }

            return Task.FromResult(string.Empty);
        }

        public Task<int> GetConcurrentViewers()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult((int)curBroadcast.Statistics.ConcurrentViewers!);
            }

            return Task.FromResult(0);
        }

        public Task<string> GetLiveChatId()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast.Snippet.LiveChatId);
            }

            return Task.FromResult(string.Empty);
        }

        public Task<string> GetLiveStreamId()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast.Id);
            }

            return Task.FromResult(string.Empty);
        }

        public Task<string> GetTitle()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast.Snippet.Title);
            }

            return Task.FromResult(string.Empty);
        }

        public Task<string> GetDescription()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast.Snippet.Description);
            }

            return Task.FromResult(string.Empty);
        }

        public Task<string> GetChannelId()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast.Snippet.ChannelId);
            }

            return Task.FromResult(string.Empty);
        }

        public Task<DateTimeOffset> GetPublishedDate()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast!.Snippet.PublishedAtDateTimeOffset ?? DateTimeOffset.MinValue);
            }

            return Task.FromResult(DateTimeOffset.MinValue);
        }

        public Task<DateTimeOffset> GetScheduledStartTime()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast.Snippet.ScheduledStartTimeDateTimeOffset ?? DateTimeOffset.MinValue);
            }

            return Task.FromResult(DateTimeOffset.MinValue);
        }

        public Task<DateTimeOffset> GetActualStartTime()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast.Snippet.ActualStartTimeDateTimeOffset ?? DateTimeOffset.MinValue);
            }

            return Task.FromResult(DateTimeOffset.MinValue);
        }

        public Task<DateTimeOffset> GetActualEndTime()
        {
            if (curBroadcast != null)
            {
                return Task.FromResult(curBroadcast.Snippet.ActualStartTimeDateTimeOffset ?? DateTimeOffset.MinValue);
            }

            return Task.FromResult(DateTimeOffset.MinValue);
        }

        public async Task<string> GetActiveLiveStreamId()
        {
            LiveBroadcastsResource.ListRequest? liveBroadcastsListRequest = _youtubeService!.LiveBroadcasts.List("id") ?? null;
            if (liveBroadcastsListRequest != null)
            {
                liveBroadcastsListRequest.BroadcastStatus = LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.Active;

                LiveBroadcastListResponse liveBroadcastsListResponse = await liveBroadcastsListRequest.ExecuteAsync();
                if (liveBroadcastsListResponse.Items.Count > 0)
                {
                    string liveStreamId = liveBroadcastsListResponse.Items[0].Id!;
                    return liveStreamId;
                }
            }

            return string.Empty; // No active live streams found
        }

        public async Task<string> GetLiveChatIdForCurrentStream(string liveStreamId)
        {
            LiveBroadcastsResource.ListRequest liveBroadcastsListRequest = _youtubeService!.LiveBroadcasts.List("snippet")!;
            liveBroadcastsListRequest.Id = liveStreamId;

            LiveBroadcastListResponse liveBroadcastsListResponse = await liveBroadcastsListRequest.ExecuteAsync()!;

            if (liveBroadcastsListResponse.Items.Count > 0)
            {
                string liveChatId = liveBroadcastsListResponse.Items[0].Snippet.LiveChatId!;
                return liveChatId;
            }

            return string.Empty; // No live chat ID found
        }

        public async Task SendLiveChatMessage(string messageText)
        {
            string liveStreamId = await GetActiveLiveStreamId()!;
            if (string.IsNullOrEmpty(liveStreamId))
            {
                await UiForm!.MessageBoxWithOK("Warning!", "No active live streams found.", "OK");
                return;
            }

            string liveChatId = await GetLiveChatIdForCurrentStream(liveStreamId);
            if (string.IsNullOrEmpty(liveChatId))
            {
                await UiForm!.MessageBoxWithOK("Warning!", "No live chat ID found.", "OK");
                return;
            }

            int maxLength = 200;
            List<string> messages = SplitMessage(messageText, maxLength);

            foreach (string message in messages)
            {
                LiveChatMessage liveChatMessage = new()
                {
                    Snippet = new LiveChatMessageSnippet
                    {
                        LiveChatId = liveChatId,
                        Type = "textMessageEvent",
                        TextMessageDetails = new LiveChatTextMessageDetails
                        {
                            MessageText = message
                        }
                    }
                };

                LiveChatMessagesResource.InsertRequest insertRequest = _youtubeService!.LiveChatMessages.Insert(liveChatMessage, "snippet")!;
                _ = await insertRequest.ExecuteAsync()!;
            }
        }

        private List<string> SplitMessage(string message, int maxLength)
        {
            List<string> messages = new();

            if (message.Length <= maxLength)
            {
                messages.Add(message);
            }
            else
            {
                int index = 0;
                while (index < message.Length)
                {
                    int length = Math.Min(maxLength, message.Length - index);

                    // Find the nearest space to split the message
                    int spaceIndex = message.LastIndexOf(' ', index + length - 1);
                    if (spaceIndex > index)
                    {
                        // Include the space in the current message
                        length = spaceIndex - index + 1;
                    }

                    string subMessage = message.Substring(index, length);
                    messages.Add(subMessage);
                    index += length;
                }
            }

            return messages;
        }

        public Task<bool> IsUserAdmin(string userId)
        {
            if (_youtubeService == null)
            {
                throw new InvalidOperationException("YouTube service is not initialized.");
            }
            var moderatorsRequest = _youtubeService.LiveChatModerators.List(curBroadcast!.Snippet.ChannelId, "snippet");
            var moderatorsResponse = moderatorsRequest.Execute();

            var moderators = moderatorsResponse.Items;

            foreach (var moderator in moderators)
            {
                if (moderator.Snippet?.ModeratorDetails?.DisplayName == userId)
                {
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        } // Needs Testing

        static LiveBroadcast GetLiveBroadcast(YouTubeService youtubeService)
        {
            LiveBroadcastsResource.ListRequest request = youtubeService.LiveBroadcasts.List("snippet")!;
            request.BroadcastStatus = LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.Active;

            LiveBroadcastListResponse? response = request.Execute();
            return response.Items.FirstOrDefault()!;
        }

        public async Task DeleteLiveChatMessage(string liveChatMessageId)
        {
            await _youtubeService!.LiveChatMessages.Delete(liveChatMessageId).ExecuteAsync();
        }

        public async Task<LiveChatModeratorListResponse> GetLiveChatModerators(string liveChatId, string? pageToken = null, int maxResults = 100)
        {
            LiveChatModeratorsResource.ListRequest liveChatModeratorsListRequest = _youtubeService!.LiveChatModerators.List(liveChatId, string.Empty)!;
            liveChatModeratorsListRequest.PageToken = pageToken;
            liveChatModeratorsListRequest.MaxResults = maxResults;
            return await liveChatModeratorsListRequest.ExecuteAsync()!;
        }

        public async Task<LiveChatModerator> InsertLiveChatModerator(string liveChatId, string channelId)
        {
            LiveChatModerator liveChatModerator = new()
            {
                Snippet = new LiveChatModeratorSnippet
                {
                    LiveChatId = liveChatId,
                    ModeratorDetails = new ChannelProfileDetails
                    {
                        ChannelId = channelId
                    }
                }
            };

            LiveChatModeratorsResource.InsertRequest insertRequest = _youtubeService!.LiveChatModerators.Insert(liveChatModerator, "snippet")!;
            return await insertRequest.ExecuteAsync();
        }

        public async Task DeleteLiveChatModerator(string liveChatModeratorId)
        {
            await _youtubeService!.LiveChatModerators.Delete(liveChatModeratorId).ExecuteAsync()!;
        }

        public async Task<string> CheckLiveStatus(string userId)
        {
            try
            {
                LiveBroadcastListResponse liveBroadcasts = await GetLiveBroadcasts(LiveBroadcastsResource.ListRequest.BroadcastStatusEnum.Active, 1)!;
                if (liveBroadcasts.Items.Count == 0)
                {
                    return string.Empty;
                }

                LiveBroadcast liveBroadcast = liveBroadcasts.Items[0]!;
                ChannelListResponse channel = await GetChannel(liveBroadcast.Snippet.ChannelId)!;
                if (channel.Items[0].Id != userId)
                {
                    return string.Empty;
                }

                string link = $"https://www.youtube.com/watch?v={liveBroadcast.Id}";
                return $"{userId} is live: {link}";
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"{e.Message}", "OK");
                return $"An error occurred while checking the live status of {userId}: {e.Message}";
            }
        }


        // Custom type classes


        public class ChatMessageEventArgs : EventArgs
        {
            public string Username { get; }
            public string Message { get; }

            public ChatMessageEventArgs(string username, string message)
            {
                Username = username;
                Message = message;
            }
        }

    }
}

#pragma warning restore CA1822 // Mark members as static