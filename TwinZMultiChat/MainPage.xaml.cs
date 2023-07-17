using Discord.WebSocket;
using Microsoft.Maui.Controls;
using CommunityToolkit.Maui.Storage;
using Newtonsoft.Json;
using TwitchLib.Client.Events;
using System.Text;
using System.IO;
using Microsoft.Maui.Dispatching;
using TwitchLib.Api.Helix;
using CommunityToolkit.Maui;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Web;
using Newtonsoft.Json.Linq;
using System.Globalization;
using Discord;
using GEmojiSharp;

namespace TwinZMultiChat;

public partial class MainPage : ContentPage
{
#region Variables
#pragma warning disable CA1822 // Mark members as static (I don't want them static)
    public readonly static string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinZMultiChat");
    private readonly static string DefaultColor = "rgb(100,255,255)";
    public readonly string StreamMsgIntro = "TMC: ";
    private readonly static object chatOverlayFileLock = new();
    private readonly static List<OverlayMsg> chatMessages = new();
    private readonly static UiData UiDat = new();
    private static UiData StartingUiDat = new();
    private Dictionary<string, int> userStrikes = new();  // Tracks the number of strikes for each user
    private HashSet<string> bannedUsers = new();  // Tracks the banned users
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
        if (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS) // Disable Overlay Generation on Android
        {
            OverlayCheckBox.IsVisible = false;
            OverlayLabel.IsVisible = false;
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
        VisualStateManager.GoToState(SaveBtn, "Normal");
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
        VisualStateManager.GoToState(ResetBtn, "Normal");
        DiscordBotTokenBox.Text = "";
        DiscordChannelIDBox.Text = "";
        TwitchChatIDBox.Text = "";
        TwitchClientIDBox.Text = "";
        TwitchClientSecretBox.Text = "";
        YouTubeApplicationNameBox.Text = "";
        OverlayLocationBox.Text = "";
        await WriteToLog("Reset Successfully.\n");
    }

    private async void SaveBotCommand_Clicked(object sender, EventArgs e)
    {
        VisualStateManager.GoToState(SaveCmdBtn, "Normal");
        if (!string.IsNullOrWhiteSpace(botResponseBox.Text) || !string.IsNullOrWhiteSpace(botCommandBox.Text))
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
        else
        {
            await MessageBoxWithOK("Failed!", "The command nor the response can be empty.");
        }
    }

    private async void DelBotCommand_Clicked(object sender, EventArgs e)
    {
        VisualStateManager.GoToState(DelCmdBtn, "Normal");
        if (!string.IsNullOrWhiteSpace(botCommandBox.Text))
        {
            string command = botCommandBox.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(command))
            {
                UiDat!.BotCommands.Remove(command);
                // Clear the input fields
                botCommandBox.Text = string.Empty;
                botResponseBox.Text = string.Empty;
                // Refresh the table view
                RefreshTableView();
            }
        }
        else
        {
            await MessageBoxWithOK("Failed!", "The command nor the response can be empty.");
        }
    }

    private async void OnStartBtn_Clicked(object sender, EventArgs e)
    {
        VisualStateManager.GoToState(StartBtn, "Normal");
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
        VisualStateManager.GoToState(StopBtn, "Normal");
        await Task.Delay(50);
        await StopAsync();
        await WriteToLog("Stopped Sync");
    }

    private async void OnLicenseBtn_Clicked(object sender, EventArgs e)
    {
        VisualStateManager.GoToState(LicenseBtn, "Normal");
        try
        {
            string LicenseText = "MIT License\r\n\r\nCommunityToolKit: Copyright (c) .NET Foundation and Contributors\r\nNewtonsoft.Json: Copyright (c) 2007 James Newton-King\r\nTwitchLib: Copyright (c) 2017 swiftyspiffy (Cole)\r\nDiscord.Net: Copyright (c) 2015-2022 Contributors\r\nGEmojiSharp: Copyright (c) 2019 Henrik Lau Eriksson\r\nTwinZMultiChat: Copyright (c) 2015-2022 Contributors\r\nAll Rights Reserved\r\n\r\nPermission is hereby granted, free of charge, to any person obtaining a copy\r\nof this software and associated documentation files (the \"Software\"), to deal\r\nin the Software without restriction, including without limitation the rights\r\nto use, copy, modify, merge, publish, distribute, sublicense, and/or sell\r\ncopies of the Software, and to permit persons to whom the Software is\r\nfurnished to do so, subject to the following conditions:\r\n\r\nThe above copyright notice and this permission notice shall be included in all\r\ncopies or substantial portions of the Software.\r\n\r\nTHE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR\r\nIMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,\r\nFITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE\r\nAUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER\r\nLIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,\r\nOUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE\r\nSOFTWARE.\r\n\r\n--------------------------------------------------------------------------------\r\nApache 2.0 License\r\n\r\nGoogle.Apis.YouTube.v3: Copyright (c) 2011-2015 Google Inc.\r\n\r\nLicensed under the Apache License, Version 2.0 (the \"License\");\r\nyou may not use this file except in compliance with the License.\r\nYou may obtain a copy of the License at\r\n\r\n    http://www.apache.org/licenses/LICENSE-2.0\r\n\r\nUnless required by applicable law or agreed to in writing, software\r\ndistributed under the License is distributed on an \"AS IS\" BASIS,\r\nWITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.\r\nSee the License for the specific language governing permissions and\r\nlimitations under the License.";
            await MessageBoxWithOK("License", LicenseText);
        }
        catch (Exception ex)
        {
            await WriteToLog($"Save Failed: {ex.Message}\n");
        }
    }

    #endregion BtnClicks

    #region UI elements
    private void CreateTransparentBrowser()
    {
        var webView = new WebView
        {
            Source = new UrlWebViewSource { Url = Path.Combine(UiDat.HtmlLocation, "ChatOverlay.html")},
            BackgroundColor = Microsoft.Maui.Graphics.Color.FromRgba(255, 255, 255, 150),
            Opacity = 0.6,
            HeightRequest = 410,
            WidthRequest = 640,
            InputTransparent = true
             
        };
        Grid contentGrid = new()
        {
            BackgroundColor = Microsoft.Maui.Graphics.Color.FromRgba(255, 255, 255, 150),
            Children = { webView }
        };
        ContentPage contentPage = new() 
        {
            Content = contentGrid
        };
        Window secondWindow = new()
        {
            Page = contentPage,
            Width = 640,  // Set the desired width for the window
            Height = 410  // Set the desired height for the window
        };
        try
        {
            Application.Current!.OpenWindow(secondWindow);
        }
        catch (Exception)
        {
        }
    }

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
            if (string.IsNullOrWhiteSpace(input))
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

    public async Task<string> MessageBoxWithYesNo(string title, string promptMessage, string confirm = "Yes", string cancel = "No")
    {
        string result = string.Empty;
        if (Dispatcher.IsDispatchRequired)
        {
            // Move the UI-related code to the UI thread
            await Application.Current!.Dispatcher.DispatchAsync(() =>
            {
                result = MessageBoxWithYesNo(title, promptMessage, confirm, cancel).Result;
            });
        }
        else
        {
            result = await Application.Current!.MainPage!.DisplayPromptAsync(title, promptMessage, confirm, cancel);
        }
        return result;
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
            await WriteToLog("Enabled Overlay Generation.");
            bool OpenTransparentWindow = false;
#if DEBUG
            OpenTransparentWindow = true;
#endif
            if (OpenTransparentWindow)
            {
                //CreateTransparentBrowser();
            }
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
                await youTubeBot!.ConnectAsyncAndroid();
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
            await ProcessBotCommand(msg.Platform, msg.Message.ToLower().Replace("!", ""), msg.User);
            return;
        }
        if (UiDat.EnableOverlay)
        {
            AddMessageToChatOverlay(msg);
        }
        if (UiDat.EnableYouTube)
        {
            await youTubeBot!.SendLiveChatMessage($"{discordBot!.StreamMsgIntro}{msg.User}: {msg.Message}");
        }
        if (UiDat.EnableTwitch)
        {
            await twitchBot!.SendMessage($"{discordBot!.StreamMsgIntro}{msg.User}: {msg.Message}");
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
            await ProcessBotCommand(msg.Platform, msg.Message.ToLower().Replace("!", ""), msg.User);
            return;
        }
        if (UiDat.EnableOverlay)
        {
            AddMessageToChatOverlay(msg);
        }
        if (UiDat.EnableDiscord)
        {
            await discordBot!.SendMessageAsync(UiDat.DiscordChannelID, $"{discordBot!.StreamMsgIntro}{msg.User}: {msg.Message}");
        }
        if (UiDat.EnableTwitch)
        {
            await twitchBot!.SendMessage($"{discordBot!.StreamMsgIntro}{msg.User} :  {msg.Message}");
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
            await ProcessBotCommand(msg.Platform, msg.Message.ToLower().Replace("!", ""), msg.User);
            return;
        }
        if (UiDat.EnableOverlay)
        {
            AddMessageToChatOverlay(msg);
        }
        if (UiDat.EnableDiscord)
        {
            await discordBot!.SendMessageAsync(UiDat.DiscordChannelID, $"{discordBot!.StreamMsgIntro}{msg.User}: {msg.Message}");
        }
        if (UiDat.EnableYouTube)
        {
            await youTubeBot!.SendLiveChatMessage($"{discordBot!.StreamMsgIntro}{msg.User}: {msg.Message}");
        }
    }

    public async Task ProcessBotCommand(string platform, string command, string username)
    {
        command = command.Replace("!", "").ToLower();

        // Perform permission check based on the platform and username
        if (!(await HasPermission(platform, username)))
        {
            await SendBotMessage(platform, "Insufficient permissions.");
            return;
        }

        if (UiDat.BotCommands.TryGetValue(command, out string? response) && response != null)
        {
            response = await ReplaceVariables(platform, response);
            await SendBotMessage(platform, response);
            return;
        }
        else
        {
            //await SendBotMessage(platform, "Unknown command.");
            return;
        }
    }

    public async Task<bool> HasPermission(string platform, string username)
    {
        if (platform == "discord" && discordBot != null)
        {
            return await discordBot.IsUserAdmin(username);
        }
        if (platform == "youtube" && youTubeBot != null)
        {
            return await youTubeBot.IsUserAdmin(username);
        }
        if (platform == "twitch" && twitchBot != null)
        {
            return await twitchBot.IsUserAdmin(username);
        }
        return false;
    }



    public async Task<string> ReplaceVariables(string platform, string text)
    {
        // Replace variables in the text based on the platform
        switch (platform.ToLower())
        {
            case "discord":
                if (text.Contains("$(user)"))
                {
                    //text = text.Replace("$(User)", ""); // Replace with user's display name
                }
                if (text.Contains("$(userlevel)"))
                {
                    //text = text.Replace("$(User.Level)", "250");
                }
                if (text.Contains("$(title)"))
                {
                    // text = text.Replace("$(Title)", ""); // Replace with channel's title
                }
                if (text.Contains("$(channel)"))
                {
                    //text = text.Replace("$(Channel)", ""); // Replace with channel's name
                }
                if (text.Contains("$(channelviewers)"))
                {
                    //text = text.Replace("$(Channel.Viewers)", "234");
                }
                break;
            case "youtube":
                if (text.Contains("$(livestreamstatus)"))
                {
                    text = text.Replace("$(livestreamstatus)", await youTubeBot!.GetLiveStreamStatus());
                }
                if (text.Contains("$(concurrentviewers)"))
                {
                    text = text.Replace("$(concurrentviewers)", (await youTubeBot!.GetConcurrentViewers()).ToString());
                }
                if (text.Contains("$(livechatid)"))
                {
                    text = text.Replace("$(livechatid)", await youTubeBot!.GetLiveChatId());
                }
                if (text.Contains("$(livestreamid)"))
                {
                    text = text.Replace("$(livestreamid)", await youTubeBot!.GetLiveStreamId());
                }
                if (text.Contains("$(title)"))
                {
                    text = text.Replace("$(title)", await youTubeBot!.GetTitle());
                }
                if (text.Contains("$(description)"))
                {
                    text = text.Replace("$(description)", await youTubeBot!.GetDescription());
                }
                if (text.Contains("$(channelid)"))
                {
                    text = text.Replace("$(channelid)", await youTubeBot!.GetChannelId());
                }
                if (text.Contains("$(publisheddate)"))
                {
                    text = text.Replace("$(publisheddate)", (await youTubeBot!.GetPublishedDate()).ToString());
                }
                if (text.Contains("$(scheduledstarttime)"))
                {
                    text = text.Replace("$(scheduledstarttime)", (await youTubeBot!.GetScheduledStartTime()).ToString());
                }
                if (text.Contains("$(actualstarttime)"))
                {
                    text = text.Replace("$(actualstarttime)", (await youTubeBot!.GetActualStartTime()).ToString());
                }
                if (text.Contains("$(actualendtime)"))
                {
                    text = text.Replace("$(actualendtime)", (await youTubeBot!.GetActualEndTime()).ToString());
                }
                if (text.Contains("$(totalviews)"))
                {
                    //text = text.Replace("$(totalviews)", (await youTubeBot!.GetTotalViews()).ToString());
                }
                if (text.Contains("$(likecount)"))
                {
                    //text = text.Replace("$(likecount)", (await youTubeBot!.GetLikeCount()).ToString());
                }
                if (text.Contains("$(dislikecount)"))
                {
                    //text = text.Replace("$(dislikecount)", (await youTubeBot!.GetDislikeCount()).ToString());
                }
                if (text.Contains("$(commentcount)"))
                {
                    //text = text.Replace("$(commentcount)", (await youTubeBot!.GetCommentCount()).ToString());
                }
                if (text.Contains("$(favoritecount)"))
                {
                    //text = text.Replace("$(favoritecount)", (await youTubeBot!.GetFavoriteCount()).ToString());
                }
                if (text.Contains("$(duration)"))
                {
                    //text = text.Replace("$(duration)", await youTubeBot!.GetDuration());
                }
                break;
            case "twitch":
                if (twitchBot != null)
                {
                    string usrID = await twitchBot!.CurUserId();
                    if (text.Contains("$(followers)"))
                    {
                        text = text.Replace("$(followers)", (await twitchBot!.GetTotalFollowers(usrID)).ToString());
                    }
                    if (text.Contains("$(subscriptions)"))
                    {
                        text = text.Replace("$(subscriptions)", (await twitchBot!.GetSubscriptions(usrID, (new List<string> { usrID })))?.Data.Length.ToString() ?? "0");
                    }
                    if (text.Contains("$(totalviews)"))
                    {
                        text = text.Replace("$(totalviews)", (await twitchBot!.GetTotalVideoViews(usrID)).ToString());
                    }
                    if (text.Contains("$(user)"))
                    {
                        text = text.Replace("$(user)", (await twitchBot!.GetChannelName(usrID)));
                    }
                    if (text.Contains("$(channel)"))
                    {
                        text = text.Replace("$(channel)", (await twitchBot!.GetChannelName(usrID)));
                    }
                    if (text.Contains("$(title)"))
                    {
                        text = text.Replace("$(title)", (await twitchBot!.GetStreamTitle(usrID)));
                    }
                    if (text.Contains("$(channelviewers)"))
                    {
                        //text = text.Replace("$(channelviewers)", "");
                    }
                    if (text.Contains("$(sender)"))
                    {
                        //text = text.Replace("$(sender)", "");
                    }// Replace with sender
                    if (text.Contains("$(sender.points)"))
                    {
                        text = text.Replace("$(sender.points)", (await twitchBot!.GetSenderBits(usrID)));
                    }// Replace with points
                    if (text.Contains("$(streamuptime)"))
                    {
                        text = text.Replace("$(streamuptime)", (await twitchBot!.GetStreamUptime(usrID)));
                    }
                }
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
    private static readonly object _overlayLock = new();
    public static string GenerateChatOverlay(List<OverlayMsg> chatMessages, int refreshIntervalInSeconds)
    {
        lock (_overlayLock)
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
                    border-radius: 5px;
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
                .emote {
                    display: inline-block;
                    width: 28px;
                    height: 28px;
                    background-repeat: no-repeat;
                    background-size: contain;
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
                if (UiDat.EnableTwitch && messageEntry.Platform == "twitch")
                {
                    message = twitchBot!.ReplaceTwitchEmotes(message).GetAwaiter().GetResult();
                }
                if (UiDat.EnableDiscord && messageEntry.Platform == "discord")
                {
                    message = discordBot!.ReplaceDiscordEmotes(message).GetAwaiter().GetResult();
                }
                message = GEmojiSharp.Emoji.Emojify(message);

                sb.AppendLine($@"<div class=""chat-message"">
                <div class=""chat-message-inner"">
                    <div class=""chat-message-avatar"">
                        <img src=""{platformIconPath}"" alt=""{platform}"" />
                    </div>
                    <div class=""chat-message-content""><span class=""user-color"" style=""color: {userColor}"">{user}</span>: <span>{message}</span></div>
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
    }

    private static string GetPlatformIconPath(string platform)
    {
        return platform.ToLower() switch
        {
            "twitch" => Path.Combine("", "Twitch.png"),
            "youtube" => Path.Combine("", "Youtube.png"),
            "discord" => Path.Combine("", "Discord.png"),
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