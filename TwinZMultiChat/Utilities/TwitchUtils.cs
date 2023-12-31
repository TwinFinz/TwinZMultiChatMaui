using TwitchLib.Client;
using TwitchLib.Api.Helix;
using TwitchLib.Client.Events;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client.Models;
using Google.Apis.Util;
using TwitchLib.Communication.Events;
using TwitchLib.Api;
using TwitchLib.Api.Interfaces;
using TwitchLib.Api.Auth;
using TwitchLib.Communication.Models;
using TwitchLib.Client.Enums;
using TwitchLib.Client.Extensions;
using TwitchLib.Communication.Clients;
using Google.Apis.Auth.OAuth2;
using System.Diagnostics;
using TwitchLib.PubSub.Models.Responses.Messages;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.Streams.GetStreams;
using TwitchLib.PubSub.Models.Responses.Messages.AutomodCaughtMessage;
using TwitchLib.Api.Helix.Models.Channels.GetChannelInformation;
using TwitchLib.Api.Helix.Models.Users.GetUserFollows;
using TwitchLib.Api.Helix.Models.Videos.GetVideos;
using TwitchLib.Api.Helix.Models.Chat.Badges.GetChannelChatBadges;
using TwitchLib.Api.Helix.Models.Chat.Emotes.GetChannelEmotes;
using TwitchLib.Api.Helix.Models.Chat.ChatSettings;
using static System.Net.Mime.MediaTypeNames;
using System.Text.RegularExpressions;
using TwitchLib.Api.Helix.Models.Channels.GetChannelVIPs;

namespace TwinZMultiChat.Utilities
{
    public class MyTwitchAPI
    {
#pragma warning disable IDE0044 // Ignore readonly warning
        public readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinZMulitChat");
        private static string TwitchClientID = "";
        private static string TwitchClientSecret = "";
        private static string TwitchChannelID = "";
        private static string TwitchChatID = "";
        private static TwitchLib.Api.Helix.Models.Users.GetUsers.User? TwitchUserID; 
        private static string TwitchRedirectUri = "http://localhost:8080/redirect/";
        private string RefreshToken = $"";
        private static List<string> Scopes = new(){"chat:read", "whispers:read", "whispers:edit", "chat:edit", "channel:moderate", "user:read:email", "channel:read:subscriptions",
            "channel:read:redemptions", "channel:read:hype_train", "channel:manage:broadcast", "channel:manage:redemptions", "channel:manage:polls", "channel:manage:predictions"};
        private static TwitchClient? _client;
        private static TwitchAPI? _twitchApi;
        private static MainPage? UiForm;
        public event EventHandler<OnMessageReceivedArgs>? ChatMessageReceived;
#pragma warning restore IDE0044

        public MyTwitchAPI(MainPage sender, string twitchClientID, string twitchClientSecret, string twitchChatID)
        {
            TwitchClientID = twitchClientID;
            TwitchClientSecret = twitchClientSecret;
            _twitchApi = new TwitchAPI();
            _twitchApi!.Settings.ClientId = TwitchClientID;
            _twitchApi!.Settings.Secret = TwitchClientSecret;
            TwitchChatID = twitchChatID;
            UiForm = sender;
        }

        public async Task ConnectAsync()
        {
            try
            {
                string authorizationUrl = GetAuthorizationCodeUrl(TwitchClientID, TwitchRedirectUri, Scopes)!;
                if (_twitchApi!.Settings.AccessToken == null)
                {
                    UiForm!.LaunchUrl(authorizationUrl);
                    ValidateCreds();
                    _twitchApi = new TwitchLib.Api.TwitchAPI();
                    _twitchApi.Settings.ClientId = TwitchClientID;
                    WebServer server = new(TwitchRedirectUri);

                    Authorization auth = (await server.Listen())!;
                    if (auth == null)
                    {
                        throw new Exception("Authentication failed.");
                    }
                    AuthCodeResponse resp = await _twitchApi.Auth.GetAccessTokenFromCodeAsync(auth!.Code, TwitchClientSecret, TwitchRedirectUri)!;
                    _twitchApi.Settings.AccessToken = resp.AccessToken;
                    RefreshResponse refresh = await _twitchApi.Auth.RefreshAuthTokenAsync(resp.RefreshToken, TwitchClientSecret)!;
                    _twitchApi.Settings.AccessToken = refresh.AccessToken;
                    TwitchUserID = (await _twitchApi!.Helix.Users.GetUsersAsync()).Users[0];
                    TwitchChannelID = TwitchUserID.Id;
                    // print out all the data we've got
                    await UiForm!.WriteToLog($"Authorization success!\nUser: {TwitchUserID.DisplayName} (id: {TwitchUserID.Id})\nAccess token: {refresh.AccessToken}\nRefresh token: {refresh.RefreshToken}\nExpires in: {refresh.ExpiresIn}\nScopes: {string.Join(", ", refresh.Scopes)}");

                }
                if (_twitchApi!.Settings.AccessToken != null)
                {
                    ConnectionCredentials credentials = new(TwitchChatID, _twitchApi!.Settings.AccessToken);
                    ClientOptions clientOptions = new()
                    {
                        MessagesAllowedInPeriod = 750,
                        ThrottlingPeriod = TimeSpan.FromSeconds(30)
                    };
                    _client = new();
                    _client.Initialize(credentials, TwitchUserID!.Login);
                    _client.DisableAutoPong = false;
                    await RegisterEvents();
                    if (_client.Connect())
                    {
                        _client.JoinChannel(TwitchUserID!.Login);
                        await SendMessage("Active");
                    }
                    else
                    {
                        await UiForm!.MessageBoxWithOK("Warning!", "Client Failed to connect", "OK"); 
                        throw new Exception("Client Failed to connect");
                    }
                }
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"Error: {e.Message}", "OK");
                await Task.FromException(e);
            }
        }
        private static string GetAuthorizationCodeUrl(string clientId, string redirectUri, List<string> scopes)
        {
            string scopesStr = string.Join(' ', scopes);

            return "https://id.twitch.tv/oauth2/authorize?" +
                   $"client_id={clientId}&" +
                   $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                   "response_type=code&" +
                   $"scope={Uri.EscapeDataString(scopesStr)}";
        }
        private async static void ValidateCreds()
        {
            if (String.IsNullOrEmpty(TwitchClientID))
                throw new Exception("client id cannot be null or empty");
            if (String.IsNullOrEmpty(TwitchClientSecret))
                throw new Exception("client secret cannot be null or empty");
            if (String.IsNullOrEmpty(TwitchRedirectUri))
                throw new Exception("redirect uri cannot be null or empty");
            await UiForm!.WriteToLog($"Client ID: '{TwitchClientID}', \nClient Secret: '{TwitchClientSecret}'\nRedirect url: '{TwitchRedirectUri}'.");
        }
        public Task DisconnectAsync()
        {
            if (_client != null && _client!.IsConnected)
            {
                DeregisterEvents();
                _client!.Disconnect();
            }
            return Task.CompletedTask;
        }

        private Task RegisterEvents()
        {
            _client!.OnMessageReceived += OnChatMessageReceived;
            _client!.OnDisconnected += OnDisconnect;
            _client!.OnConnected += OnConnect;
            _client!.OnNewSubscriber += OnNewSubscriber;
            _client!.OnGiftedSubscription += OnGiftedSubscription;
            _client!.OnReSubscriber += OnReSubscriber;
            _client!.OnContinuedGiftedSubscription += OnContinuedGiftedSubscription;
            return Task.CompletedTask;
        }
        private Task DeregisterEvents()
        {
            _client!.OnMessageReceived -= OnChatMessageReceived;
            _client!.OnDisconnected -= OnDisconnect;
            _client!.OnConnected -= OnConnect;
            _client!.OnNewSubscriber -= OnNewSubscriber;
            _client!.OnGiftedSubscription -= OnGiftedSubscription;
            _client!.OnReSubscriber -= OnReSubscriber;
            _client!.OnContinuedGiftedSubscription -= OnContinuedGiftedSubscription;
            return Task.CompletedTask;
        }

        #region EventFunctions
        private async void OnConnect(object? sender, OnConnectedArgs e)
        {
            await UiForm!.WriteToLog($"Twitch Connected: {e.BotUsername} {e.AutoJoinChannel}");
        }
        private async void OnDisconnect(object? sender, OnDisconnectedEventArgs e)
        {
            await UiForm!.WriteToLog($"Twitch Disconnected");

        }
        private void OnNewSubscriber(object? sender, TwitchLib.Client.Events.OnNewSubscriberArgs e)
        {
            // Handle new subscriber event
            string subscriberUsername = e.Subscriber.DisplayName;
            //int months = e.Subscriber.Months;
            // You can perform actions based on the new subscriber event
        } // Fix
        private void OnGiftedSubscription(object? sender, OnGiftedSubscriptionArgs e)
        {
            throw new NotImplementedException();
        } // Fix
        private void OnReSubscriber(object? sender, OnReSubscriberArgs e)
        {
            throw new NotImplementedException();
        }// Fix
        private void OnContinuedGiftedSubscription(object? sender, OnContinuedGiftedSubscriptionArgs e)
        {
            throw new NotImplementedException();
        }// Fix
        protected virtual void OnChatMessageReceived(object? sender, OnMessageReceivedArgs e)
        {
            ChatMessageReceived?.Invoke(this, e);
        }
        #endregion

        public async Task SendMessage(string message)
        {
            try
            {
                if (_client!.JoinedChannels.Count < 1)
                {
                    _client!.JoinChannel(_client!.TwitchUsername);
                }
                if (_client != null && _client.IsConnected)
                {
                    _client.SendMessage(_client!.TwitchUsername, message); // Send the message to the joined channel
                }
                else
                {
                    throw new Exception("Twitch Client Not Connected.");
                }
                await Task.Delay(0); // Fake Delay
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                throw new Exception(e.Message);
            }
        }
        public async Task<TwitchLib.Api.Helix.Models.Users.GetUsers.GetUsersResponse?> GetUsers(List<string> userLogins)
        {
            try
            {
                return await _twitchApi!.Helix.Users.GetUsersAsync(userLogins);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return null;
            }
        }
        public async Task<TwitchLib.Api.Helix.Models.Streams.GetStreams.GetStreamsResponse?> GetStream(List<string> userIds)
        {
            try
            {
                return await _twitchApi!.Helix.Streams.GetStreamsAsync(userIds: userIds);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return null;
            }
        }
        public async Task<TwitchLib.Api.Helix.Models.Channels.GetChannelInformation.GetChannelInformationResponse?> GetChannels(string channelIds)
        {
            try
            {
                return await _twitchApi!.Helix.Channels.GetChannelInformationAsync(channelIds);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return null;
            }
        }
        public async Task<TwitchLib.Api.Helix.Models.Search.SearchChannelsResponse?> SearchChannels(string query, int limit = 20)
        {
            try
            {
                return await _twitchApi!.Helix.Search.SearchChannelsAsync(query, true, null, limit);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"{e.Message}", "OK");
                return null;
            }
        }
        public async Task<TwitchLib.Api.Helix.Models.Users.GetUserFollows.GetUsersFollowsResponse?> GetFollowers(string userId, int first = 20)
        {
            try
            {
                return await _twitchApi!.Helix.Users.GetUsersFollowsAsync(toId: userId, first: first);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"{e.Message}", "OK");
                return null;
            }
        }
        public async Task<TwitchLib.Api.Helix.Models.Videos.GetVideos.GetVideosResponse?> GetVideos(string userId, int limit = 20)
        {
            try
            {
                return await _twitchApi!.Helix.Videos.GetVideosAsync(userId: userId, first: limit);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"{e.Message}", "OK");
                return null;
            }
        }
        public async Task<TwitchLib.Api.Helix.Models.Subscriptions.GetUserSubscriptionsResponse?> GetSubscriptions(string userId, List<string> UserIds)
        {
            try
            {
                return await _twitchApi!.Helix.Subscriptions.GetUserSubscriptionsAsync(userId, UserIds);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"{e.Message}", "OK");
                return null;
            }
        }
        public async Task<TwitchLib.Api.Helix.Models.Games.GetGamesResponse?> GetGames(List<string> gameIds)
        {
            try
            {
                return await _twitchApi!.Helix.Games.GetGamesAsync(gameIds);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"{e.Message}", "OK");
                return null;
            }
        }
        public async Task<TwitchLib.Api.Helix.Models.Games.GetTopGamesResponse?> GetTopGames(int limit = 20)
        {
            try
            {
                return await _twitchApi!.Helix.Games.GetTopGamesAsync(first: limit);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"{e.Message}", "OK");
                return null;
            }
        }
        public async Task<string> GetChannelName(string userId)
        {
            try
            {
                var channelResponse = await _twitchApi!.Helix.Channels.GetChannelInformationAsync(userId);
                if (channelResponse != null)
                {
                    return channelResponse.Data[0].BroadcasterName;
                }
                return string.Empty;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return string.Empty;
            }
        }
        public async Task<string> GetStreamUptime(string userId)
        {
            try
            {
                var streamResponse = await _twitchApi!.Helix.Streams.GetStreamsAsync(userIds: new List<string> { userId });
                if (streamResponse != null && streamResponse.Streams.Length > 0)
                {
                    DateTime startTime = streamResponse.Streams[0].StartedAt;
                    DateTime currentTime = DateTime.UtcNow;
                    TimeSpan uptime = currentTime - startTime;

                    // Format the uptime as desired
                    string formattedUptime = uptime.ToString(@"dd\.hh\:mm\:ss");
                    return formattedUptime;
                }
                return string.Empty;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return string.Empty;
            }
        }
        public async Task<DateTime?> GetNextScheduledStream(string userId)
        {
            try
            {
                var streamResponse = await _twitchApi!.Helix.Streams.GetStreamsAsync(userIds: new List<string> { userId });
                if (streamResponse != null && streamResponse.Streams.Length > 0)
                {
                    var stream = streamResponse.Streams[0];
                    return stream.StartedAt;
                }
                return null;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return null;
            }
        }
        public async Task<string> GetStreamLink(string userId)
        {
            try
            {
                var channelResponse = GetChannelName(userId);
                if (channelResponse != null)
                {
                    return $"https://www.twitch.tv/{channelResponse}";
                }
                return string.Empty;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return string.Empty;
            }
        }
        public async Task<int> GetTotalVideoViews(string userId)
        {
            try
            {
                GetVideosResponse videosResponse = await _twitchApi!.Helix.Videos.GetVideosAsync(userId: userId);
                if (videosResponse.Videos.Length > 0)
                {
                    int total = 0;
                    object viewLock = new();

                    Parallel.ForEach(videosResponse.Videos, video =>
                    {
                        int viewCount = video.ViewCount;
                        lock (viewLock)
                        {
                            total += viewCount;
                        }
                    });

                    return total;
                }
                return 0;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return 0;
            }
        }
        public async Task<long> GetTotalFollowers(string userId)
        {
            try
            {
                GetUsersFollowsResponse followersResponse = await _twitchApi!.Helix.Users.GetUsersFollowsAsync(toId: userId, first: 1);
                if (followersResponse.TotalFollows > 0)
                {
                    return followersResponse.TotalFollows;
                }
                return 0;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return 0;
            }
        }
        public async Task<string> GetStreamTitle(string userId)
        {
            if (_twitchApi == null || userId == null)
            {
                throw new Exception("Twitch API is not initialized or Twitch Channel ID is null.");
            }
            var stream = await _twitchApi!.Helix.Streams.GetStreamsAsync(userIds: new List<string> { userId });
            if (stream.Streams.Length > 0)
            {
                return stream.Streams[0].Title;
            }
            else
            {
                throw new Exception("No active stream found.");
            }
        }
        public async Task<int> GetChannel(string userId)
        {
            try
            {
                GetChannelInformationResponse channelResponse = await _twitchApi!.Helix.Channels.GetChannelInformationAsync(userId);
                return channelResponse.Data.Length;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return 0;
            }
        }
        public async Task<GetChatSettingsResponse?> GetChatRulesAsync(string channelId)
        {
            try
            {
                return await _twitchApi!.Helix.Chat.GetChatSettingsAsync(channelId, channelId);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return null;
            }
        }
        public async Task<string> GetUserId(string username)
        {
            try
            {
                GetUsersResponse usersResponse = await _twitchApi!.Helix.Users.GetUsersAsync(logins: new List<string> { username });
                if (usersResponse.Users.Length >= 0)
                {
                    return usersResponse.Users[0].Id;
                }
                return string.Empty;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"{e.Message}", "OK");
                return string.Empty;
            }
        }
        public async Task<string> CurUserId()
        {
            await Task.Delay(0);
            return TwitchUserID!.Id;
        }
        public async Task<string> IsStreamLive(string twitchID, string BotUserName)
        {
            try
            {
                GetUsersResponse usersResponse = await _twitchApi!.Helix.Users.GetUsersAsync(new List<string> { twitchID })!;
                var user = usersResponse.Users.FirstOrDefault()!;
                if (user == null)
                {
                    return $"{BotUserName} not found.";
                }

                GetStreamsResponse streamsResponse = await _twitchApi!.Helix.Streams.GetStreamsAsync(userIds: new List<string> { user.Id })!;
                var stream = streamsResponse.Streams.FirstOrDefault();
                if (stream == null)
                {
                    return string.Empty;
                }

                return $"{BotUserName} is live: https://www.twitch.tv/{stream.UserName.ToUpper()}";
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", $"{e.Message}", "OK");
                return $"An error occurred checking if {BotUserName} is live.";
            }
        }
        public async Task<GetChannelChatBadgesResponse?> GetChatBadgesAsync(string channelId)
        {
            try
            {
                return await _twitchApi!.Helix.Chat.GetChannelChatBadgesAsync(channelId);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return null;
            }
        }
        public async Task<GetChannelEmotesResponse?> GetChatEmotesAsync(string channelId)
        {
            try
            {
                return await _twitchApi!.Helix.Chat.GetChannelEmotesAsync(channelId);
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return null;
            }
        }
        public static async Task<string> GetBadgeIconPathFromTwitchMessage(TwitchLibMessage twitchMessage)
        {
            if (twitchMessage!.Badges != null && twitchMessage!.Badges.Count > 0)
            {
                foreach (var badge in twitchMessage.Badges)
                {
                    // Check for the desired badge information
                    if (badge.Key == "subscriber" && badge.Value == "1")
                    {
                        // Retrieve the badge images from Twitch API
                        string badgeUrl = await GetBadgeImageUrlFromTwitch(badge.Key, badge.Value);
                        return badgeUrl;
                    }
                    else if (badge.Key == "moderator")
                    {
                        // Retrieve the badge images from Twitch API
                        string badgeUrl = await GetBadgeImageUrlFromTwitch(badge.Key, badge.Value);
                        return badgeUrl;
                    }
                    // Add more badge checks if needed
                }
            }

            return string.Empty; // No matching badge found
        }
        public static async Task<string> GetBadgeImageUrlFromTwitch(string badgeId, string version)
        {
            string apiUrl = $"https://api.twitch.tv/kraken/chat/{TwitchChannelID}/badges/{badgeId}/versions/{version}";

            using (HttpClient client = new())
            {
                client.DefaultRequestHeaders.Add("Client-ID", "your-client-id"); // Replace with your Twitch Client ID
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.twitchtv.v5+json");

                HttpResponseMessage response = client.GetAsync(apiUrl).Result;
                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    dynamic jsonResult = JsonConvert.DeserializeObject(responseContent) ?? string.Empty;
                    string badgeImageUrl = jsonResult["badge_sets"][badgeId]["versions"][version]["image_url_1x"];

                    return badgeImageUrl;
                }
                else
                {
                    // Handle the case when the API request fails
                    // You can log the error, throw an exception, or return a default badge image URL
                    return string.Empty;
                }
            }
        }
        public async Task<string> ReplaceTwitchEmotes(string message)
        {
            var channelEmotesResponse = await _twitchApi!.Helix.Chat.GetChannelEmotesAsync(TwitchChannelID);
            var globalEmotesResponse = await _twitchApi!.Helix.Chat.GetGlobalEmotesAsync();
            string urlPrefix = "https://static-cdn.jtvnw.net/emoticons/v2/";
            string urlSuffix = "/default/dark/1.0";

            if (channelEmotesResponse != null)
            {
                foreach (var emote in channelEmotesResponse.ChannelEmotes)
                {
                    string emoteCode = emote.Name;
                    string emoteUrl = emote.Images.Url2X;

                    // Replace emote code with image tag
                    message = Regex.Replace(message, $@"\b{Regex.Escape(emoteCode)}\b", $@"<img alt=""{emoteCode}"" class=""chat-image chat-line__message--emote"" src=""{emoteUrl}"" style=""width: 40px; height: 40px;"">");
                }
            }
            if (globalEmotesResponse != null)
            {
                foreach (var emote in globalEmotesResponse.GlobalEmotes)
                {
                    string emoteCode = emote.Name;
                    string emoteUrl = $"{urlPrefix}{emote.Id}{urlSuffix}";

                    // Replace emote code with image tag
                    message = Regex.Replace(message, $@"\b{Regex.Escape(emoteCode)}\b", $@"<img alt=""{emoteCode}"" class=""chat-image chat-line__message--emote"" src=""{emoteUrl}"" style=""width: 40px; height: 40px;"">");
                }
            }
            return message;
        }


        public async Task<int> GetTotalSubscribers(string userId)
        {
            try
            {
                var subscriptionsResponse = await _twitchApi!.Helix.Subscriptions.GetUserSubscriptionsAsync(userId, (new List<string>()));
                if (subscriptionsResponse != null)
                {
                    return subscriptionsResponse.Data.Length;
                }
                return 0;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return 0;
            }
        } // broken

        public async Task<string> GetSenderBits(string userId)
        {
            try
            {
                var cheermotesResponse = await _twitchApi!.Helix.Bits.GetCheermotesAsync(userId);
                if (cheermotesResponse != null && cheermotesResponse.Listings.Length > 0)
                {
                    // Assuming you want to retrieve the bits from the first cheermote
                    int senderBits = cheermotesResponse.Listings[0].Order;
                    return senderBits.ToString();
                }
                return string.Empty;
            }
            catch (Exception e)
            {
                await UiForm!.MessageBoxWithOK("Warning!", e.Message, "OK");
                return string.Empty;
            }
        } // Fix this

        public async Task GetEmoteLink()
        {
            string channelId = ""; // Replace with the actual channel ID
            GetChannelChatBadgesResponse? badgesResponse = await GetChatBadgesAsync(channelId);
            GetChannelEmotesResponse? emotesResponse = await GetChatEmotesAsync(channelId);

            if (badgesResponse != null && badgesResponse.EmoteSet != null && badgesResponse.EmoteSet.Count() > 0)
            {
                // Replace $(Badge) with the badge URL or any desired value
                foreach (var badgeSet in badgesResponse!.EmoteSet!)
                {
                    foreach (var version in badgeSet.Versions)
                    {
                        string badgeVariable = $"$(Badge.{badgeSet.SetId}.{version.Id})";
                        string badgeValue = version.ImageUrl1x; // Replace with the desired value from the badge
                        //text = text.Replace(badgeVariable, badgeValue);
                    }
                }
            }

            if (emotesResponse != null && emotesResponse.ChannelEmotes != null && emotesResponse.ChannelEmotes.Count() > 0)
            {
                // Replace $(Emote) with the emote URL or any desired value
                foreach (var emote in emotesResponse!.ChannelEmotes!)
                {
                    string emoteVariable = $"$(Emote.{emote.Name})";
                    string emoteValue = emote.Images.Url1X; // Replace with the desired value from the emote
                    //text = text.Replace(emoteVariable, emoteValue);
                }
            }
        } // Needs work

        public async Task<bool> IsUserAdmin(string username)
        {
            if (_client == null || _twitchApi == null)
            {
                throw new Exception("Twitch client or API is not initialized.");
            }
            if (username == null)
            {
                return false;
            }

            var vips = await _twitchApi.Helix.Channels.GetVIPsAsync(TwitchChannelID);
            var adminUsernames = vips.Data.Select(vip => vip.UserName.ToLowerInvariant());
            return adminUsernames.Contains(username.ToLowerInvariant());
        } // Needs Testing

        public class AccessTokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; } = int.MinValue;

            [JsonProperty("refresh_token")]
            public string RefreshToken { get; set; } = string.Empty;
        }
        class TwitchAccessToken
        {
            [JsonProperty("access_token")]
            public string? AccessToken { get; set; }
            [JsonProperty("expires_in")]
            public int? ExpiresIn { get; set; }
            [JsonProperty("token_type")]
            public string? TokenType { get; set; }
        }
    }
}
