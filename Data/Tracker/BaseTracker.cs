using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.WebSocket;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using MopsBot.Data.Tracker.APIResults;
using OxyPlot;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Attributes;

namespace MopsBot.Data.Tracker
{
    [BsonIgnoreExtraElements]
    public abstract class BaseTracker : MopsBot.Api.BaseAPIContent, IDisposable
    {
        //Avoid ratelimit by placing a gap between all trackers.
        public static int ExistingTrackers = 0;
        public enum TrackerType { Twitch, TwitchClip, Twitter, Osu, Overwatch, Youtube, YoutubeLive, Reddit, News, WoW, OSRS, HTML };
        private bool disposed = false;
        private SafeHandle handle = new SafeFileHandle(IntPtr.Zero, true);
        protected System.Threading.Timer checkForChange;
        public event MainEventHandler OnMajorEventFired;
        public event MinorEventHandler OnMinorEventFired;
        public delegate Task MinorEventHandler(ulong channelID, BaseTracker self, string notificationText);
        public delegate Task MainEventHandler(ulong channelID, Embed embed, BaseTracker self, string notificationText = "");
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
        public Dictionary<ulong, string> ChannelMessages;

        [BsonId]
        public string Name;

        public BaseTracker(int interval, int gap = 5000)
        {
            ExistingTrackers++;
            ChannelMessages = new Dictionary<ulong, string>();
            checkForChange = new System.Threading.Timer(CheckForChange_Elapsed, new System.Threading.AutoResetEvent(false),
                                                                                (gap % interval) + 5000, interval);
        }

        public virtual void PostInitialisation()
        {
        }

        protected abstract void CheckForChange_Elapsed(object stateinfo);

        public virtual string TrackerUrl()
        {
            return null;
        }

        protected async Task OnMajorChangeTracked(ulong channelID, Embed embed, string notificationText = "")
        {
            if (OnMajorEventFired != null)
                await OnMajorEventFired(channelID, embed, this, notificationText);
        }
        protected async Task OnMinorChangeTracked(ulong channelID, string notificationText)
        {
            if (OnMinorEventFired != null)
                await OnMinorEventFired(channelID, this, notificationText);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                handle.Dispose();
                checkForChange.Dispose();
            }

            disposed = true;
        }

        public override Dictionary<string, object> GetParameters(ulong guildId)
        {
            string[] channels = Program.Client.GetGuild(guildId).TextChannels.Select(x => $"#{x.Name}:{x.Id}").ToArray();

            return new Dictionary<string, object>(){
                {"Parameters", new Dictionary<string, object>(){
                                {"Name", ""}, 
                                {"Notification", "New content!"}, 
                                {"Channel", channels}}}
            };
        }

        public override object GetAsScope(ulong channelId){
            return new ContentScope(){
                Name = this.Name,
                Notification = this.ChannelMessages[channelId],
                Channel = "#" + ((SocketGuildChannel)Program.Client.GetChannel(channelId)).Name + ":" + channelId
            };
        }

        public override void Update(params string[] args){
            var channelId = ulong.Parse(args[2].Split(":")[1]);
            ChannelMessages[channelId] = args[1];
        }

        public new struct ContentScope
        {
            public string Name;
            public string Notification;
            public string Channel;
        }
    }
}