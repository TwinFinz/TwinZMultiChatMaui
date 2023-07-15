# TwinZMultiChatMaui - Multi-Platform Multi-Stream chatbot/sync utility. (just because i can)
TwinZMultiChat But even better, updated UI and bunches of bug fixes


This code utilizes these libraries among others:

TwitchLib: [https://github.com/TwitchLib/TwitchLib](https://github.com/TwitchLib/TwitchLib)

Discord.Net: [https://github.com/discord-net/Discord.Net](https://github.com/discord-net/Discord.Net)

Youtube.V3: [https://developers.google.com/youtube/v3](https://developers.google.com/youtube/v3)



OBS Overlay: 

Height: 620

Width: 440



You will need to get the following:

Client ID and Client Secret from your Twitch developer account: [https://dev.twitch.tv/console](https://dev.twitch.tv/console)

Youtube.v3 OAuth 2.0 client keys (client_secret.json): [https://console.cloud.google.com/apis/credentials](https://console.cloud.google.com/apis/credentials)

Discord Bot Token: [https://discord.com/developers/applications](https://discord.com/developers/applications)

(Discord bot needs to be added to your server by using the Generate URL with "Bot" enabled. I used Admin but read and write permissions should suffice)

Discord Channel ID(the one you want to use for the synced chat): Go into Discord settings and turn on "Developer Mode", Navigate to the channel you want to use. Right Click and copy the Channel ID.



Enter the required info into the UI Enable the services you want to use and Save. If you are using Youtube enter the Application Name you set in the [Google Console](https://console.cloud.google.com/apis/credentials) 

It will prompt you for the client_secrets.json file apon start.

Bot commands are currently a "command":"response" only (I am working on getting the variable swapping functional for each platform)

The overlay will be saved to whatever location you set apon activation(Remember to save)

There is a known issue where the log window does not auto scroll on Windows. I am not sure why it won't but am looking to fix it.



Hope this is helpful to someone. We are currently waiting for validation from the windows store and a link will be posted soon. :)



Android Users:

The Youtube Authorization is currently broken and will be fixed shortly, all other services still function correctly. (landscape mode not locked but prefered)

i am currently waiting on google Validation and a link will be available shortly. :)
