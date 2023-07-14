using Discord.WebSocket;
using Microsoft.Maui.Controls;
using CommunityToolkit.Maui.Storage;
using Newtonsoft.Json;
using TwitchLib.Client.Events;
using System.Text;
using System.IO;
using Microsoft.Maui.Controls.Compatibility;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Dispatching;
using TwitchLib.Api.Helix.Models.Chat.Badges.GetChannelChatBadges;
using TwitchLib.Api.Helix.Models.Chat.Emotes.GetChannelEmotes;
using TwitchLib.Api.Helix;
using CommunityToolkit.Maui;

namespace TwinZMultiChat;

public partial class MainPage : ContentPage
{
    #region Variables

    //private const string BotUsername = "TwinZMultiChat";

#pragma warning disable CA1822 // Mark members as static (I don't want them static)
    public readonly static string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinZMulitChat");
    private readonly static string DefaultColor = "rgb(100,255,255)";
    public readonly string StreamMsgIntro = "TMC: ";
    private readonly static object chatOverlayFileLock = new();
    private readonly static List<OverlayMsg> chatMessages = new();
    private readonly static UiData UiDat = new();
    private static UiData StartingUiDat = new();
    private Dictionary<string, int> userStrikes = new();  // Tracks the number of strikes for each user
    private HashSet<string> bannedUsers = new ();  // Tracks the banned users
    private const int MaxStrikes = 3;  // Maximum number of strikes before a user gets banned


    private static Utilities.MyDiscordAPI? discordBot;
    private static Utilities.MyTwitchAPI? twitchBot;
    private static Utilities.MyYoutubeAPI? youTubeBot;
    #endregion Variables

    #region Init
    public MainPage()
    {
        InitializeComponent();
        // Set the data context to the page itself
        BindingContext = this;
        this.Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object? sender, EventArgs e)
    {
        LoadSavedVariables();
        if (DeviceInfo.Platform == DevicePlatform.WinUI || DeviceInfo.Platform == DevicePlatform.macOS) // Work around for Editor (EG: LogBox) not updating correctly when text is changed
        {
            Window window = this.Window; // Get the window
            // Get Display size reported
            DisplayInfo displayInfo = DeviceDisplay.MainDisplayInfo;
            double screenWidth = displayInfo.Width;
            double screenHeight = displayInfo.Height;
            // Resize the window
            double windowWidth = (int)(displayInfo.Width * 0.5);
            double windowHeight = (int)(displayInfo.Height * 0.5);
            window.Height = windowHeight;
            window.Width = windowWidth;
            // Set window location
            double centerX = (screenWidth - windowWidth) / 2;
            double centerY = (screenHeight - windowHeight) / 2;
            window.X = centerX;
            window.Y = centerY;
        }
    }

    public async Task WriteToLogDispatch(string Message)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            await Dispatcher.DispatchAsync(async () =>
            {
                await WriteToLog(Message);
            });
        }
        else
        {
            UiDat.LogText += Message + Environment.NewLine;
            LogBox.Text += UiDat.LogText;
        }
    }

    public async Task WriteToLog(string Message)
    {
        if (!MainThread.IsMainThread)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await WriteToLog(Message);
            });
        }
        else
        {
            UiDat.LogText += Message + "\n";
            LogBox.Text = UiDat.LogText;
        }
        await Task.Delay(0); // Fake Delay
    }

    private async void LoadSavedVariables()
    {
#if DEBUG
#endif
        BindingContext = this;
        
        try
        {
            string cfgInput = Preferences.Get("SavedUiData", string.Empty);
            if (cfgInput != string.Empty)
            {
                UiData? SavedUiData = JsonConvert.DeserializeObject<UiData>(cfgInput);

                if (SavedUiData != null)
                {
                    UiDat.DiscordBotToken = SavedUiData.DiscordBotToken;
                    DiscordBotTokenBox.Text = UiDat.DiscordBotToken;

                    UiDat.DiscordChannelID = SavedUiData.DiscordChannelID;
                    DiscordChannelIDBox.Text = UiDat.DiscordChannelID.ToString();

                    UiDat.TwitchChatID = SavedUiData.TwitchChatID;
                    TwitchChatIDBox.Text = UiDat.TwitchChatID;

                    UiDat.TwitchClientID = SavedUiData.TwitchClientID;
                    TwitchClientIDBox.Text = UiDat.TwitchClientID;

                    UiDat.TwitchClientSecret = SavedUiData.TwitchClientSecret;
                    TwitchClientSecretBox.Text = UiDat.TwitchClientSecret;

                    UiDat.YouTubeApplicationName = SavedUiData.YouTubeApplicationName;
                    YouTubeApplicationNameBox.Text = UiDat.YouTubeApplicationName;

                    UiDat.HtmlLocation = SavedUiData.HtmlLocation;
                    OverlayLocationBox.Text = UiDat.HtmlLocation;

                    UiDat.EnableDiscord = SavedUiData.EnableDiscord;
                    DiscordCheckBox.IsChecked = UiDat.EnableDiscord;

                    UiDat.EnableYouTube = SavedUiData.EnableYouTube;
                    YouTubeCheckBox.IsChecked = UiDat.EnableYouTube;

                    UiDat.EnableTwitch = SavedUiData.EnableTwitch;
                    TwitchCheckBox.IsChecked = UiDat.EnableTwitch;

                    UiDat.EnableOverlay = SavedUiData.EnableOverlay;
                    OverlayCheckBox.IsChecked = UiDat.EnableOverlay;

                    UiDat.BotCommands = SavedUiData.BotCommands;
                    RefreshTableView();
                    await WriteToLog("Loaded Successfully.");
                }
            }
            else
            {
                await WriteToLog("Config Not Found.");
            }
        }
        catch (JsonException ex)
        {
            await WriteToLog($"Failed to deserialize JSON: {ex.Message}");
        }
        
    }
#endregion

#region BtnClicks
    private async void OnSaveBtn_Clicked(object sender, EventArgs e)
    {
        string JsonOutput = "";
        JsonOutput += JsonConvert.SerializeObject(UiDat);
        try
        {
            Preferences.Set("SavedUiData", JsonOutput);

            await WriteToLog($"Saved Successfully.\n");
        }
        catch (Exception ex)
        {
            await WriteToLog($"Save Failed: {ex.Message}\n");
        }
    }

    private async void OnResetBtn_Clicked(object sender, EventArgs e)
    {
        DiscordBotTokenBox.Text = "";
        DiscordChannelIDBox.Text = "";
        TwitchChatIDBox.Text = "";
        TwitchClientIDBox.Text = "";
        TwitchClientSecretBox.Text = "";
        YouTubeApplicationNameBox.Text = "";
        OverlayLocationBox.Text = "";
#if DEBUG
#else
        //File.Delete(Path.Combine(DataFolder, "Config.xml"));
#endif
        await WriteToLog("Reset Successfully.\n");
    }
    
    private void SaveBotCommand_Clicked(object sender, EventArgs e)
    {
        string command = botCommandBox.Text.Trim().ToLower(); 
        string response = botResponseBox.Text.Trim();

        if (!string.IsNullOrEmpty(command) && !string.IsNullOrEmpty(response))
        {
            // Add or update the command/response pair in the dictionary
            if (!UiDat!.BotCommands.ContainsKey(command))
            {
                UiDat!.BotCommands.Add(command, response);
            }
            else
            {
                UiDat!.BotCommands[command] = response;
            }

            // Clear the input fields
            botCommandBox.Text = string.Empty;
            botResponseBox.Text = string.Empty;

            // Refresh the table view
            RefreshTableView();
            OnSaveBtn_Clicked(this, new EventArgs());

        }
    }

    private async void OnStartBtn_Clicked(object sender, EventArgs e)
    {
        try
        {
            StartingUiDat = new UiData() 
            { 
                EnableDiscord = UiDat.EnableDiscord,
                EnableYouTube = UiDat.EnableYouTube,
                EnableTwitch = UiDat.EnableTwitch,
                EnableKick = UiDat.EnableKick
            };
            await StartAsync();
        }
        catch (Exception ex)
        {
            await WriteToLog(ex.Message);
        }
        await WriteToLog("Started Sync");
    }

    private async void OnStopBtn_Clicked(object sender, EventArgs e)
    {
        await Task.Delay(50);
        await StopAsync();
        await WriteToLog("Stopped Sync");
    }
#endregion BtnClicks

#region UI elements
    private void DiscordBotTokenBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UiDat.DiscordBotToken = e.NewTextValue ?? "";
    }

    private async void DiscordChannelIDBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (e.OldTextValue == "0")
        {
            return;
        }
        // Rest of the code for validation and handling the TextChanged event
        if (ulong.TryParse(e.NewTextValue, out ulong channelID))
        {
            if (DiscordChannelIDBox.Text.Length <= 16) // Check if the value has approximately 18 digits
            {
                await MessageBoxWithOK("Sorry", "Channel ID must be approximately 18 digits.", "OK");
            }
            else
            {
                UiDat.DiscordChannelID = channelID;
            }
        }
        else
        {
            await MessageBoxWithOK("Sorry", "Invalid Channel ID format.\nChannel ID must be a number approximately 18 digits long.", "OK");
        }
    }

    private void YouTubeApplicationNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UiDat.YouTubeApplicationName = e.NewTextValue ?? "";
    }

    private void TwitchChatIDBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UiDat.TwitchChatID = e.NewTextValue ?? "";
    }

    private void TwitchClientIDBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UiDat.TwitchClientID = e.NewTextValue ?? "";
    }

    private void TwitchClientSecretBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UiDat.TwitchClientSecret = e.NewTextValue ?? "";
    }

    private void OverlayLocationBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UiDat.HtmlLocation = e.NewTextValue ?? "";
    }

    private void DiscordCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        UiDat.EnableDiscord = e.Value;
    }

    private void YouTubeCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        UiDat.EnableYouTube = e.Value;
    }

    private void TwitchCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        UiDat.EnableTwitch = e.Value;
    }

    private void KickCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        UiDat.EnableKick = e.Value;
    }

    private async void OverlayCheckBox_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        UiDat.EnableOverlay = e.Value;
        if (UiDat.EnableOverlay)
        {
            if (UiDat.HtmlLocation == string.Empty)
            {
                UiDat.HtmlLocation = await PickFolder();
                OverlayLocationBox.Text = UiDat.HtmlLocation;
            }
            OverlayLocationBox.IsVisible = true;
            OverlayLocationBoxLabel.IsVisible = true;
        }
        else
        {
            OverlayLocationBox.IsVisible = false;
            OverlayLocationBoxLabel.IsVisible = false;
        }
    }

    private void RefreshTableView()
    {
        tableLayout.Children.Clear();

        foreach (var commandResponsePair in UiDat!.BotCommands)
        {
            Label commandLabel = new()
            {
                Text = commandResponsePair.Key,
                FontAttributes = FontAttributes.Bold
            };
            Label responseLabel = new()
            {
                Text = commandResponsePair.Value,
                Margin = new Thickness(0, 5, 0, 10)
            };

            tableLayout.Children.Add(commandLabel);
            tableLayout.Children.Add(responseLabel);
        }
    }

    public async Task<string?> MessageBoxWithInput(string title, string promptMessage, string confirm = "OK", string cancel = "Cancel")
    {
        if (Dispatcher.IsDispatchRequired)
        {
            // Move the UI-related code to the UI thread
            await Application.Current!.Dispatcher.DispatchAsync(async () =>
            {
                await this.MessageBoxWithInput(title, promptMessage, confirm, cancel);
                return true;
            });
        }
        else
        {
            string input = await Application.Current!.MainPage!.DisplayPromptAsync(title, promptMessage, confirm, cancel);
            if (string.IsNullOrEmpty(input))
            {
                throw new Exception("User Canceled Authorization");
            }

            return input;
        }
        return string.Empty;
    }

    public async Task<bool> MessageBoxWithOK(string title, string promptMessage, string cancel = "OK")
    {
        if (Dispatcher.IsDispatchRequired)
        {
            // Move the UI-related code to the UI thread
            await Application.Current!.Dispatcher.DispatchAsync(() =>
            {
                return true;
            });
        }
        else
        {
            await Application.Current!.MainPage!.DisplayAlert(title, promptMessage, cancel);
        }
        return false;
    }

    public async Task<bool> MessageBoxWithYesNo(string title, string promptMessage, string confirm = "Yes", string cancel = "No")
    {
        if (Dispatcher.IsDispatchRequired)
        {
            // Move the UI-related code to the UI thread
            await Application.Current!.Dispatcher.DispatchAsync(() =>
            {
                return true;
            });
        }
        else
        {
            await Application.Current!.MainPage!.DisplayPromptAsync(title, promptMessage, confirm, cancel);
        }
        return false;
    }

    private static bool IsFolderWritable(string folderPath)
    {
        try
        {
            // Try creating a new file in the folder
            string testFilePath = System.IO.Path.Combine(folderPath, "test.txt");
            using (var fileStream = System.IO.File.Create(testFilePath))
            {
                fileStream.Close();
                System.IO.File.Delete(testFilePath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> PickFolder()
    {
        string startpath = Environment.CurrentDirectory;
        if (UiDat.HtmlLocation != string.Empty)
        {
            startpath = UiDat.HtmlLocation;
        }

        CancellationTokenSource source = new();
        CancellationToken token = source.Token;
        var selectedFolder = await FolderPicker.Default.PickAsync(startpath, token);
        if (selectedFolder != null)
        {
            string folderPath = selectedFolder.Folder!.Path;
            if (IsFolderWritable(folderPath))
            {
                return folderPath;
            }
        }
        return string.Empty;
    }

    public async void LaunchUrl(string url)
    {
        try
        {
            await Launcher.OpenAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            // Handle any exceptions if the browser launch fails
            await this.MessageBoxWithOK("Warning!", $"Failed to launch the default web browser: {ex.Message}", "OK");
        }
    }
#endregion

#region MessageUtils
    public async Task StartAsync()
    {
        if (UiDat.EnableOverlay) // Connect Twitch
        {
            try
            {
                await MessageBoxWithOK("Select Folder", "Location not found. Please select a location to save the generated HTML file.");

            }
            catch (Exception ex)
            {
                await MessageBoxWithOK("Warning!", ex.Message, "OK");
            }
            await WriteToLog("Enabled Overlay Generation.");
        }
            if (UiDat.EnableTwitch) // Connect Twitch
        {
            twitchBot = new(this, UiDat.TwitchClientID, UiDat.TwitchClientSecret, UiDat!.TwitchChatID);
            await WriteToLog("Connecting to Twitch.");
            await twitchBot!.ConnectAsync();
            await twitchBot!.SendMessage($"{UiDat.TwitchChatID}: Active");
            twitchBot!.ChatMessageReceived += SyncMessageTwitch;
            await WriteToLog("Success.");
        }
        if (UiDat.EnableDiscord) // Connect Discord
        {
            discordBot = new(this, UiDat.DiscordBotToken, UiDat.DiscordChannelID, StreamMsgIntro);
            await WriteToLog("Connecting to Discord.");
            await discordBot!.ConnectAsync();
            await WriteToLog("Success.");
            discordBot.ChatMessageReceived += SyncMessageDiscord;
        }
        if (UiDat.EnableYouTube) // Connect YouTube
        {
            youTubeBot = new(this, UiDat.YouTubeApplicationName, StreamMsgIntro);
            await WriteToLog("Connecting to YouTube.");
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
#if DEBUG
                await youTubeBot!.ConnectAsyncSecondary();
                await this.MessageBoxWithOK("Warning!", $"This is currently broken on android. Sorry, Hope to get it fixed soon.", "OK");
#else
                await this.MessageBoxWithOK("Warning!", $"This is currently broken on android. Sorry, Hope to get it fixed soon.", "OK");
#endif
            }
            else
            {
                await youTubeBot!.ConnectAsync(); // All other OS.
            }
            youTubeBot!.ChatMessageReceived += SyncMessageYouTube;
            await youTubeBot!.SendLiveChatMessage($"{UiDat.YouTubeApplicationName}: Active");
            await WriteToLog("Success.");
        }
        if (UiDat.EnableKick)
        {
            await WriteToLog("We have not yet implemented Kick. Sorry");
        }
    }

    public async Task StopAsync()
    {
        if (StartingUiDat.EnableDiscord) // Connect Discord
        {
            await WriteToLog("Disconnecting From Discord.");
            discordBot!.ChatMessageReceived -= SyncMessageDiscord;
            await discordBot!.DisconnectAsync();
            await WriteToLog("Success.");
        }
        if (StartingUiDat.EnableYouTube) // Connect YouTube
        {
            await WriteToLog("Disconnecting From YouTube.");
            youTubeBot!.ChatMessageReceived -= SyncMessageYouTube;
            await youTubeBot!.DisconnectAsync();
            await WriteToLog("Success.");

        }
        if (StartingUiDat.EnableTwitch) // Connect Twitch
        {
            await WriteToLog("Disconnecting From Twitch.");
            twitchBot!.ChatMessageReceived -= SyncMessageTwitch;
            await twitchBot!.DisconnectAsync();
            await WriteToLog("Success.");
        }
    }

    public async void SyncMessageDiscord(object? sender, SocketMessage message)
    {
        await WriteToLog($"MessageReceived| Discord> {message.Author}: {message}");
        string Color = await discordBot!.GetDiscordUsernameColor(message.Author.Id) ?? DefaultColor;
        OverlayMsg msg = new()
        {
            Platform = "discord",
            User = message.Author.Username,
            UserColor = Color,
            Message = message.Content
        };
        if (message.Content.StartsWith("!"))
        {
            string command = message.Content.ToLower();
            await ProcessBotCommand("discord", command);
            return;
        }
        if (UiDat.EnableOverlay)
        {
            AddMessageToChatOverlay(msg);
        }
        if (UiDat.EnableYouTube)
        {
            await youTubeBot!.SendLiveChatMessage($"{discordBot!.StreamMsgIntro}{message.Author.Username}: {message.Content}");
        }
        if (UiDat.EnableTwitch)
        {
            await twitchBot!.SendMessage($"{discordBot!.StreamMsgIntro}{message.Author.Username}: {message.Content}");
        }
    }

    public async void SyncMessageYouTube(object? sender, Utilities.MyYoutubeAPI.ChatMessageEventArgs YTMessage)
    {
        await WriteToLog($"MessageReceived| YouTube> {YTMessage.Username}: {YTMessage.Message}");
        OverlayMsg msg = new()
        {
            Platform = "youtube",
            User = YTMessage.Username,
            UserColor = DefaultColor,
            Message = YTMessage.Message
        };
        if (YTMessage.Message.StartsWith("!"))
        {
            string command = YTMessage.Message.ToLower();
            await ProcessBotCommand("youtube", command);
            return;
        }
        if (UiDat.EnableOverlay)
        {
            AddMessageToChatOverlay(msg);
        }
        if (UiDat.EnableDiscord)
        {
            await discordBot!.SendMessageAsync(UiDat.DiscordChannelID, $"{discordBot!.StreamMsgIntro}{YTMessage.Username}: {YTMessage.Message}");
        }
        if (UiDat.EnableTwitch)
        {
            await twitchBot!.SendMessage($"{discordBot!.StreamMsgIntro}{YTMessage.Username}: {YTMessage.Message}");
        }
    }

    public async void SyncMessageTwitch(object? sender, OnMessageReceivedArgs TwitchMsg)
    {
        await WriteToLog($"MessageReceived| Twitch> {TwitchMsg.ChatMessage.Username}: {TwitchMsg.ChatMessage.Message}");
        OverlayMsg msg = new()
        {
            Platform = "twitch",
            User = TwitchMsg.ChatMessage.Username,
            UserColor = TwitchMsg.ChatMessage.ColorHex ?? string.Empty,
            Message = TwitchMsg.ChatMessage.Message
        };
        if (TwitchMsg.ChatMessage.Message.StartsWith("!"))
        {
            string command = TwitchMsg.ChatMessage.Message.ToLower();
            await ProcessBotCommand("twitch", command.Replace("!", ""));
            return;
        }
        if (UiDat.EnableOverlay)
        {
            AddMessageToChatOverlay(msg);
        }
        if (UiDat.EnableDiscord)
        {
            await discordBot!.SendMessageAsync(UiDat.DiscordChannelID, $"{discordBot!.StreamMsgIntro}{TwitchMsg.ChatMessage.Username}: {TwitchMsg.ChatMessage.Message}");
        }
        if (UiDat.EnableYouTube)
        {
            await youTubeBot!.SendLiveChatMessage($"{discordBot!.StreamMsgIntro}{TwitchMsg.ChatMessage.Username}: {TwitchMsg.ChatMessage.Message}");
        }
    }

    // Function to process bot commands
    public async Task ProcessBotCommand(string platform, string command)
    {
        command = command.Replace("!","").ToLower();
        if (UiDat.BotCommands.TryGetValue(command, out string? response) && response != null)
        {
            response = await ReplaceVariables(platform, response);
            await SendBotMessage(platform, response);
        }
        else
        {
            await SendBotMessage(platform, "Unknown command.");
        }
    }

    public async Task<string> ReplaceVariables(string platform, string text)
    {
        // Replace variables in the text based on the platform
        switch (platform.ToLower())
        {
            case "discord":
                //text = text.Replace("$(User)", ""); // Replace with user's display name
                //text = text.Replace("$(User.Level)", "250");
                //text = text.Replace("$(Title)", ""); // Replace with channel's title
                //text = text.Replace("$(Channel)", ""); // Replace with channel's name
                //text = text.Replace("$(Channel.Viewers)", "234");
                break;
            case "youtube":
                text = text.Replace("$(livestreamstatus)", await youTubeBot!.GetLiveStreamStatus());
                text = text.Replace("$(concurrentviewers)", (await youTubeBot!.GetConcurrentViewers()).ToString());
                text = text.Replace("$(livechatid)", await youTubeBot!.GetLiveChatId());
                text = text.Replace("$(livestreamid)", await youTubeBot!.GetLiveStreamId());
                text = text.Replace("$(title)", await youTubeBot!.GetTitle());
                text = text.Replace("$(description)", await youTubeBot!.GetDescription());
                text = text.Replace("$(channelid)", await youTubeBot!.GetChannelId());
                text = text.Replace("$(channeltitle)", await youTubeBot!.GetChannelTitle());
                text = text.Replace("$(publisheddate)", (await youTubeBot!.GetPublishedDate()).ToString());
                text = text.Replace("$(scheduledstarttime)", (await youTubeBot!.GetScheduledStartTime()).ToString());
                text = text.Replace("$(actualstarttime)", (await youTubeBot!.GetActualStartTime()).ToString());
                text = text.Replace("$(actualendtime)", (await youTubeBot!.GetActualEndTime()).ToString());
                //text = text.Replace("$(totalviews)", (await youTubeBot!.GetTotalViews()).ToString());
                //text = text.Replace("$(likecount)", (await youTubeBot!.GetLikeCount()).ToString());
                //text = text.Replace("$(dislikecount)", (await youTubeBot!.GetDislikeCount()).ToString());
                //text = text.Replace("$(commentcount)", (await youTubeBot!.GetCommentCount()).ToString());
                //text = text.Replace("$(favoritecount)", (await youTubeBot!.GetFavoriteCount()).ToString());
                //text = text.Replace("$(duration)", await youTubeBot!.GetDuration());
                break;
            case "twitch":
                string usrID = await twitchBot!.CurUserId();
                text = text.Replace("$(Followers)", (await twitchBot!.GetTotalFollowers(usrID)).ToString());
                text = text.Replace("$(Subscriptions)", (await twitchBot!.GetSubscriptions(usrID, new List<string>{ usrID }))!.Data.Length.ToString());
                text = text.Replace("$(TotalViews)", (await twitchBot!.GetTotalVideoViews(usrID)).ToString());
                text = text.Replace("$(User)", (await twitchBot!.GetChannelName(usrID)));
                text = text.Replace("$(Channel)", (await twitchBot!.GetChannelName(usrID))); // Replace with channel's name
                //text = text.Replace("$(Title)", ""); // Replace with channel's title
                //text = text.Replace("$(Channel.viewers)", "");
                //text = text.Replace("$(Sender)", ""); // Replace with sender
                //text = text.Replace("$(Sender.Points)", "123"); // Replace with points
                //text = text.Replace("$(StreamLength)", "LCS has been streaming for 15 minutes!");
                break;
            case "kick":
                break;
            default:
                // Platform not supported, handle it accordingly
                /* Examples based from StreamElementsList
                 * text = text.Replace("${Followers}", twitchBot!.GetTotalFollowers(usrID).Result.ToString());
                 * text = text.Replace("${user}", "StreamElements");
                 * text = text.Replace("${user.level}", "250");
                 * text = text.Replace("${sender}", senderDisplayName);
                 * text = text.Replace("${sender.points}", "123");
                 * text = text.Replace("${touser}", "nuuls");
                 * text = text.Replace("${title}", "Playing Spyro!");
                 * text = text.Replace("${status}", "Playing Spyro!");
                 * text = text.Replace("${channel}", "streamelements");
                 * text = text.Replace("${channel.viewers}", "234");
                 * text = text.Replace("${channel.display_name}", "OnSlAuGhT");
                 * text = text.Replace("${lasttweet.USER}", "LCS Starting in 15 minutes!");
                 * text = text.Replace("${args}", "!channel");
                 * text = text.Replace("${args.word}", "StreamElements");
                 * text = text.Replace("${twitchemotes}", "Subscriber emotes: stylerXD, stylerRIP");
                 * text = text.Replace("${7tvemotes}", "7TV emotes: modCheck, peepoLeave, GIGACHAD, Joel, donowall");
                 * text = text.Replace("${bttvemotes}", "BTTV emotes: FeelsBadMan, FeelsGoodMan");
                 * text = text.Replace("${ffzemotes}", "FFZ emotes: peepoBlush, Bedge");
                 * text = text.Replace("${msgid}", "e9c554c5-d6c4-40e2-8e7f-fd489bc9a568");
                 * text = text.Replace("${math}", "70");
                 * text = text.Replace("${1:}", "message after command"); 
                 * text = text.Replace("${customapi.link-to-api.com}", "Outlook good.");
                 * text = text.Replace("${queryescape}", "google.com/search?q=dank+memes");
                 * text = text.Replace("${pathescape}", "api.com/%F0%9F%85%B1");
                 * text = text.Replace("${random.20-30}", "24");
                 * text = text.Replace("${random.pick}", "rare pepe");
                 * text = text.Replace("${count deaths}", "245");
                 * text = text.Replace("${count deaths +5}", "250");
                 * text = text.Replace("${count deaths -5}", "245");
                 * text = text.Replace("${count deaths 300}", "300");
                 * text = text.Replace("${getcount deaths}", "300");
                 * text = text.Replace("${plugdj_current.your_plug_dj_room}", "Fakear - Silver (Møme Remix) https://youtu.be/wzborQLuf3E");
                 * text = text.Replace("${dubtrack_current.your_dubtrack_room}", "Sia - Chandelier (Alternative♂Version) https://youtu.be/kOCxHu_F5xo");
                 * text = text.Replace("${time.CET}", "15:04");
                 * text = text.Replace("${time.until 19:00}", "next stream in 7 hours 59 mins");
                 * text = text.Replace("${uptime shroud}", "1 hour 6 mins");
                 * text = text.Replace("${repeat 3 ${1}}", "Kappa Kappa Kappa");
                 * text = text.Replace("${quote}", "#3 some dank quote");
                 * text = text.Replace("${quote 6}", "#6 some other dank quote");
                 * 
                 */
                break;
        }

        return text;
    } // Needs Work

    public async Task SendBotMessage(string platform, string message)
    {
        platform = platform.ToLower();

        if (platform == "all")
        {
            await discordBot!.SendMessageAsync(UiDat.DiscordChannelID, message);
            await twitchBot!.SendMessage(message);
            await youTubeBot!.SendLiveChatMessage(message);
        }
        else if (platform == "discord")
        {
            await discordBot!.SendMessageAsync(UiDat.DiscordChannelID, message);
        }
        else if (platform == "youtube")
        {
            await youTubeBot!.SendLiveChatMessage(message);
        }
        else if (platform == "twitch")
        {
            await twitchBot!.SendMessage(message);
        }
        else if (platform == "kick") 
        {
            // await kickBot!.SendMessage(message);
        }// needs implementing
        else
        {
            // Platform not supported, handle it accordingly
        }
    }
#endregion

#region OBS Overlay
    public static string GenerateChatOverlay(List<OverlayMsg> chatMessages, int refreshIntervalInSeconds)
    {
        StringBuilder sb = new();
        sb.AppendLine(@"<style>
                    body {
                        background-color: rgba(0, 0, 0, 0.85);
                        color: white;
                        font-family: Arial, sans-serif;
                        font-size: 18px;
                        width: 610px;
                        height: 410px;
                        opacity: 0.85;
                        position: fixed;
                        top: 0;
                        left: 0;
                        overflow-y: auto;
                        padding-right: 10px;
                    }

                    .chat-message {
                        margin-bottom: 10px;
                        background-color: #1a1a1a;
                        padding: 5px; /* Adjust the padding value to make the border smaller */
                border - radius: 5px;
                        box-shadow: 0 2px 5px rgba(0, 0, 0, 0.5);
                    }

                    .chat-message-inner {
                        display: flex;
                        align-items: center;
                        gap: 10px;
                    }

                    .chat-message-avatar {
                        width: 45px;
                        height: 45px;
                        border-radius: 50%;
                        overflow: hidden;
                        flex-shrink: 0;
                    }

                    .chat-message-avatar img {
                        width: 100%;
                        height: 100%;
                        object-fit: cover;
                    }

                    .chat-message-content {
                        flex-grow: 1;
                        word-wrap: break-word;
                    }

                    .user-color {
                        color: inherit;
                    }
                </style>");

        sb.AppendLine("<body>");
        foreach (OverlayMsg messageEntry in chatMessages)
        {
            string platform = messageEntry.Platform;
            string message = messageEntry.Message;
            string user = messageEntry.User;
            string userColor = messageEntry.UserColor;
            string platformIconPath = GetPlatformIconPath(platform.ToLower());

            sb.AppendLine($@"    <div class=""chat-message"">
                    <div class=""chat-message-inner"">
                        <div class=""chat-message-avatar"">
                            <img src=""{platformIconPath}"" alt=""{platform}"" />
                        </div>
                        <div class=""chat-message-content""><span class=""user-color"" style=""color: {userColor}"">{user}</span>: {message}</div>
                    </div>
                </div>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine($@"<script>
                setTimeout(function() {{
                    location.reload();
                }}, {refreshIntervalInSeconds * 1000});
            </script>");
        return sb.ToString();
    }

    private static string GetPlatformIconPath(string platform)
    {
        // Modify this method to return the file path or URL of the corresponding platform icon
        return platform switch
        {
            "twitch" => Path.Combine(DataFolder, "twitch.png"),
            "youtube" => Path.Combine(DataFolder, "youtube.png"),
            "discord" => Path.Combine(DataFolder, "discord.png"),
            _ => string.Empty,// Return an empty string or default icon path if platform is not recognized
        };
    }

    public static void AddMessageToChatOverlay(OverlayMsg message)
    {
        chatMessages.Add(message);
        if (chatMessages.Count > 6)
        {
            chatMessages.RemoveAt(0);
        }
        string overlayHtml = GenerateChatOverlay(chatMessages, 1);
        lock (chatOverlayFileLock)
        {
            string filePath = Path.Combine(UiDat.HtmlLocation, "ChatOverlay.html");
            File.WriteAllText(filePath, overlayHtml);
        }
    }

    public class OverlayMsg
    {
        [JsonProperty("platform")]
        public string Platform { get; set; } = string.Empty;
        [JsonProperty("user")]
        public string User { get; set; } = string.Empty;
        [JsonProperty("user-color")]
        public string UserColor { get; set; } = string.Empty; // Color format: Hexadecimal: #ffffff, RGB: RGB(255, 255, 255), RGBA: RGBA(255, 255, 255, 255)
        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
    }
#endregion
}

public class UiData // Data for Syncing with UI
{
    [JsonProperty("discord_bot_token")]
    public string DiscordBotToken { get; set; } = string.Empty;
    [JsonProperty("discord_channel_Iid")]
    public ulong DiscordChannelID { get; set; } = ulong.MinValue;
    [JsonProperty("youtube_application_name")]
    public string YouTubeApplicationName { get; set; } = string.Empty;
    [JsonProperty("html_location")]
    public string HtmlLocation { get; set; } = string.Empty;
    [JsonProperty("twitch_client_is")]
    public string TwitchClientID { get; set; } = string.Empty;
    [JsonProperty("twitch_client_secret")]
    public string TwitchClientSecret { get; set; } = string.Empty;
    [JsonProperty("twitch_chat_id")]
    public string TwitchChatID { get; set; } = string.Empty;
    [JsonProperty("enable_discord")]
    public bool EnableDiscord { get; set; } = false;
    [JsonProperty("enable_youtube")]
    public bool EnableYouTube { get; set; } = false;
    [JsonProperty("enable_twitch")]
    public bool EnableTwitch { get; set; } = false;
    [JsonProperty("enable_kick")]
    public bool EnableKick { get; set; } = false;
    [JsonProperty("enable_openai")]
    public bool EnableOverlay { get; set; } = false;
    [JsonProperty("bot_commands")]
    public Dictionary<string, string> BotCommands { get; set; } = new();
    [JsonProperty("bot_command")]
    public string BotCommand { get; set; } = string.Empty;
    [JsonProperty("bot_response")]
    public string BotResponse { get; set; } = string.Empty;
    [JsonProperty("log_text")]
    public string LogText { get; set; } = string.Empty;
}

#pragma warning restore CA1822 // Mark members as static
/* Example Preferences Get/Set
 * // getter
 * var value = Preferences.Get("nameOfSetting", "defaultValueForSetting");
 * // setter
 * Preferences.Set("nameOfSetting", value);
 */