using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.WebSocket;
using Discord.Commands;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MopsBot.Data.Tracker.APIResults.Youtube;
using System.Xml;

namespace MopsBot.Data.Tracker
{
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    public class YoutubeTracker : BaseTracker
    {
        public int UploadCount = -1;
        private string channelThumbnailUrl, uploadPlaylistId;

        public YoutubeTracker() : base()
        {
        }

        public YoutubeTracker(string channelId) : base()
        {
            Name = channelId;

            //Check if person exists by forcing Exceptions if not.
            try
            {
                var checkExists = fetchChannel().Result;
                Name = checkExists.id;
                uploadPlaylistId = checkExists.contentDetails.relatedPlaylists.uploads;
                channelThumbnailUrl = checkExists.snippet.thumbnails.medium.url;
                SetTimer();
            }
            catch (Exception e)
            {
                Dispose();
                throw new Exception($"Channel {TrackerUrl()} could not be found on Youtube!\nPerhaps you used the channel-name instead?", e);
            }
        }

        private async Task<Video[]> fetchPlaylist(int count)
        {
            var tmpResult = await FetchJSONDataAsync<Playlist>($"https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&maxResults=50&playlistId={uploadPlaylistId}&key={Program.Config["Youtube"]}");

            switchKeys();
            var orderedResult = tmpResult.items.OrderBy(x => x.snippet.publishedAt);
            return orderedResult.TakeLast(count).ToArray();
        }

        private async Task<int> fetchPlaylistCount()
        {
            if (!playlistCountCache.TryGetValue(uploadPlaylistId, out int count))
            {
                var tmpResult = await FetchJSONDataAsync<PlaylistCounts>($"https://www.googleapis.com/youtube/v3/playlists?part=contentDetails&id={uploadPlaylistId}&key={Program.Config["Youtube"]}");
                switchKeys();

                count = tmpResult.items.FirstOrDefault()?.contentDetails?.itemCount ?? 0;
                playlistCountCache[uploadPlaylistId] = count;
                await Program.MopsLog(new LogMessage(LogSeverity.Warning, "", "Had to load playlist count, current count: " + playlistCountCache.Count));
            }
            return count;
        }

        private static Dictionary<string, int> playlistCountCache = new Dictionary<string, int>();
        private static bool loading = false;
        private async static Task fetchPlaylistCountBatch()
        {
            if (!loading)
                loading = true;
            else
                return;

            while (true)
            {
                var allTrackers = StaticBase.Trackers[TrackerType.Youtube].GetTrackers().Select(x => x.Value).ToList();
                await Program.MopsLog(new LogMessage(LogSeverity.Info, "", $"Loading playlist caches"));
                for (int i = 0; i < allTrackers.Count; i += 50)
                {
                    try
                    {
                        string currentBatch = string.Join(",", allTrackers.Skip(i).Take(50).Select(x => (x as YoutubeTracker).uploadPlaylistId));
                        var tmpResult = await FetchJSONDataAsync<PlaylistCounts>($"https://www.googleapis.com/youtube/v3/playlists?part=contentDetails&maxResults=50&id={currentBatch}&key={Program.Config["Youtube"]}");
                        foreach (var playlist in tmpResult.items)
                        {
                            playlistCountCache[playlist.id] = playlist.contentDetails.itemCount;
                        }
                    }
                    catch (Exception e)
                    {
                        await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Error loading playlist caches, repeating", e));
                        i -= 50;
                        await Task.Delay(5000);
                        continue;
                    }

                    await Program.MopsLog(new LogMessage(LogSeverity.Info, "", $"Loaded {playlistCountCache.Count} playlist caches"));
                    //Be a bit faster than Timer to make cache available before request
                    await Task.Delay((600000 / allTrackers.Count) * 45);
                    switchKeys();
                }
            }
        }

        private async Task<ChannelItem> fetchChannel()
        {
            if (!channelCache.TryGetValue(Name, out ChannelItem channel))
            {
                var tmpResult = await FetchJSONDataAsync<Channel>($"https://www.googleapis.com/youtube/v3/channels?part=contentDetails,snippet&id={Name}&key={Program.Config["Youtube"]}");

                switchKeys();

                channel = tmpResult.items.First();
                channelCache[Name] = channel;
                await Program.MopsLog(new LogMessage(LogSeverity.Warning, "", "Had to load channel cache, current count: " + channelCache.Count));
            }
            return channel;
        }

        private static Dictionary<string, ChannelItem> channelCache = new Dictionary<string, ChannelItem>();
        public async static Task fetchChannelsBatch()
        {
            var trackerDict = StaticBase.Trackers[TrackerType.Youtube].GetTrackers();
            var trackerList = StaticBase.Trackers[TrackerType.Youtube].GetTrackers().Select(x => x.Value).ToList();
            await Program.MopsLog(new LogMessage(LogSeverity.Info, "", "Loading channel caches"));

            for (int i = 0; i < trackerList.Count; i += 50)
            {
                try
                {
                    string currentBatch = string.Join(",", trackerList.Skip(i).Take(50).Select(x => x.Name));
                    var tmpResult = await FetchJSONDataAsync<Channel>($"https://www.googleapis.com/youtube/v3/channels?part=contentDetails,snippet&maxResults=50&id={currentBatch}&key={Program.Config["Youtube"]}");
                    foreach (var channel in tmpResult.items)
                    {
                        channelCache[channel.id] = channel;
                        (trackerDict[channel.id] as YoutubeTracker).uploadPlaylistId = channel.contentDetails.relatedPlaylists.uploads;
                        (trackerDict[channel.id] as YoutubeTracker).channelThumbnailUrl = channel.snippet.thumbnails.medium.url;
                    }
                }
                catch (Exception e)
                {
                    await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Error loading channel caches, repeating", e));
                    i -= 50;
                    await Task.Delay(5000);
                    continue;
                }

                switchKeys();
                Task.Run(() => fetchPlaylistCountBatch().Wait());
                await Program.MopsLog(new LogMessage(LogSeverity.Info, "", $"Loaded {channelCache.Count} channel caches"));
                await Task.Delay((600000 / trackerList.Count) * 40);
            }
        }

        protected async override void CheckForChange_Elapsed(object stateinfo)
        {
            try
            {
                int repetition = 0;
                while (!channelCache.ContainsKey(Name) || !playlistCountCache.ContainsKey(uploadPlaylistId))
                {
                    if(repetition == 10){
                        await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Error for {Name}, cache was not loaded after 10 attemps."));
                        return;
                    }
                    await Task.Delay(5000);
                    repetition++;
                }

                var count = playlistCountCache[uploadPlaylistId];

                if (UploadCount == -1)
                {
                    UploadCount = count;
                    await UpdateTracker();
                }

                if (count > UploadCount)
                {
                    var newVideos = await fetchPlaylist(count - UploadCount);

                    foreach (Video video in newVideos)
                    {
                        foreach (ulong channel in ChannelConfig.Keys.ToList())
                        {
                            await OnMajorChangeTracked(channel, await createEmbed(video), (string)ChannelConfig[channel]["Notification"]);
                        }
                    }

                    UploadCount = count;
                    await UpdateTracker();
                }
            }
            catch (Exception e)
            {
                if (!e.Message.Contains("Sequence contains no elements"))
                    await Program.MopsLog(new LogMessage(LogSeverity.Error, "", $" error by {Name}", e));
                else
                    await Program.MopsLog(new LogMessage(LogSeverity.Verbose, "", $"Found no videos by {Name}"));
            }
        }

        private async Task<Embed> createEmbed(Video video)
        {
            EmbedBuilder e = new EmbedBuilder();
            e.Color = new Color(0xFF0000);
            e.Title = video.snippet.title;
            e.Url = $"https://www.youtube.com/watch?v={video.snippet.resourceId.videoId}";
            e.Timestamp = video.snippet.publishedAt;

            EmbedFooterBuilder footer = new EmbedFooterBuilder();
            footer.IconUrl = "http://www.stickpng.com/assets/images/580b57fcd9996e24bc43c545.png";
            footer.Text = "Youtube";
            e.Footer = footer;

            EmbedAuthorBuilder author = new EmbedAuthorBuilder();
            author.Name = video.snippet.channelTitle;
            author.Url = $"https://www.youtube.com/channel/{video.snippet.channelId}";
            author.IconUrl = channelThumbnailUrl;
            e.Author = author;

            e.ThumbnailUrl = channelThumbnailUrl;
            e.ImageUrl = video.snippet.thumbnails.high.url;
            e.Description = video.snippet.description.Length > 300 ? video.snippet.description.Substring(0, 300) + " [...]" : video.snippet.description;

            return e.Build();
        }

        public override string TrackerUrl()
        {
            return "https://www.youtube.com/channel/" + Name;
        }

        public override async Task UpdateTracker()
        {
            await StaticBase.Trackers[TrackerType.Youtube].UpdateDBAsync(this);
        }

        private static void switchKeys()
        {
            var tmp = Program.Config["Youtube"];
            Program.Config["Youtube"] = Program.Config["Youtube2"];
            Program.Config["Youtube2"] = tmp;
        }
    }
}