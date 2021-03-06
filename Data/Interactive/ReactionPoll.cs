﻿using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MopsBot.Data.Entities;

namespace MopsBot.Data.Interactive
{
    public class ReactionPoll
    {
        //Key: Channel ID, Value: Message IDs
        public Dictionary<ulong, List<Poll>> Polls = new Dictionary<ulong, List<Poll>>();

        public ReactionPoll()
        {
            //using (StreamReader read = new StreamReader(new FileStream($"mopsdata//ReactionPoll.json", FileMode.OpenOrCreate)))
            //{
            try
            {
                //Polls = JsonConvert.DeserializeObject<Dictionary<ulong, List<Poll>>>(read.ReadToEnd());
                //StaticBase.Database.GetCollection<MongoKVP<ulong, List<Poll>>>(this.GetType().Name).InsertMany(MongoKVP<ulong, List<Poll>>.DictToMongoKVP(Polls));
                Polls = new Dictionary<ulong, List<Poll>>(StaticBase.Database.GetCollection<MongoKVP<ulong, List<Poll>>>(this.GetType().Name).FindSync(x => true).ToList().Select(x => (KeyValuePair<ulong, List<Poll>>)x));
            }
            catch (Exception e)
            {
                Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"Could not read poll database", e)).Wait();
            }
            //}

            Polls = Polls ?? new Dictionary<ulong, List<Poll>>();

            foreach (var channel in Polls.ToList())
            {
                foreach (var poll in channel.Value.ToList())
                {
                    try
                    {
                        var textmessage = (IUserMessage)((ITextChannel)Program.Client.GetChannel(channel.Key)).GetMessageAsync(poll.MessageID).Result;

                        for (int i = 0; i < poll.Options.Length; i++)
                        {
                            var option = poll.Options[i];
                            Program.ReactionHandler.AddHandler(textmessage, EmojiDict[i], x => AddVote(x, option)).Wait();
                        }

                        Program.ReactionHandler.AddHandler(textmessage, new Emoji("🗑"), DeletePoll).Wait();
                    }
                    catch (Exception e)
                    {
                        Program.MopsLog(new LogMessage(LogSeverity.Error, "", $"[{channel.Key}][{poll.MessageID}] could not be loaded", e)).Wait();
                        if ((e.Message.Contains("Object reference not set to an instance of an object.") || e.Message.Contains("Value cannot be null."))
                            && Program.Client.ConnectionState.Equals(ConnectionState.Connected))
                        {
                            Program.MopsLog(new LogMessage(LogSeverity.Warning, "", $"Removing [{channel.Key}][{poll.MessageID}] due to missing message.")).Wait();

                            /*if (channel.Value.Count > 1){
                                channel.Value.Remove(poll);
                                UpdateDBAsync(channel.Key).Wait();
                            }
                            else{
                                Polls.Remove(channel.Key);
                                RemoveFromDBAsync(channel.Key).Wait();
                            }*/
                        }
                    }
                }
            }
        }

        public async Task InsertIntoDBAsync(ulong key)
        {
            await StaticBase.Database.GetCollection<MongoKVP<ulong, List<Poll>>>(this.GetType().Name).InsertOneAsync(MongoKVP<ulong, List<Poll>>.KVPToMongoKVP(new KeyValuePair<ulong, List<Poll>>(key, Polls[key])));
        }

        public async Task UpdateDBAsync(ulong key)
        {
            await StaticBase.Database.GetCollection<MongoKVP<ulong, List<Poll>>>(this.GetType().Name).ReplaceOneAsync(x => x.Key == key, MongoKVP<ulong, List<Poll>>.KVPToMongoKVP(new KeyValuePair<ulong, List<Poll>>(key, Polls[key])));
        }

        public async Task RemoveFromDBAsync(ulong key)
        {
            await StaticBase.Database.GetCollection<MongoKVP<ulong, List<Poll>>>(this.GetType().Name).DeleteOneAsync(x => x.Key == key);
        }

        public async Task AddPoll(ITextChannel channel, Poll poll)
        {
            EmbedBuilder e = new EmbedBuilder();
            e.Title = poll.Question;
            e.Description = $"To vote for an option, press the corresponding digit reactions.\n" +
                            "If you can manage messages, you may delete this poll by pressing the 🗑 Icon.";
            e.Color = Color.Blue;
            e.WithCurrentTimestamp();
            e.WithFooter(x => x.WithIconUrl("http://thebullelephant.com/wp-content/uploads/2016/10/poll-box-1.png").WithText("Poll"));

            StringBuilder optionText = new StringBuilder();
            for (int i = 0; i < poll.Options.Length; i++)
            {
                optionText.AppendLine(EmojiDict[i].Name + " : " + poll.Options[i]);
            }
            e.AddField("Options", optionText);

            var message = await channel.SendMessageAsync("", embed: e.Build());
            for (int i = 0; i < poll.Options.Length; i++)
            {
                var option = poll.Options[i];
                Program.ReactionHandler.AddHandler(message, EmojiDict[i], x => AddVote(x, option)).Wait();
            }

            await Program.ReactionHandler.AddHandler(message, new Emoji("🗑"), DeletePoll);
            poll.MessageID = message.Id;

            if (Polls.ContainsKey(channel.Id))
            {
                Polls[channel.Id].Add(poll);
                await UpdateDBAsync(channel.Id);
            }
            else
            {
                Polls.Add(channel.Id, new List<Poll> { poll });
                await InsertIntoDBAsync(channel.Id);
            }

            await updateMessage(message, poll);
        }

        public async Task AddVote(ReactionHandlerContext context, string option)
        {
            var poll = Polls[context.Channel.Id].First(x => x.MessageID.Equals(context.Message.Id));
            if (!poll.Voters.ContainsKey(context.Reaction.UserId))
            {
                poll.Voters.Add(context.Reaction.UserId, option);
            }
            else
            {
                poll.Voters[context.Reaction.UserId] = option;
            }

            await UpdateDBAsync(context.Channel.Id);
            await updateMessage(context, poll);
        }

        private async Task DeletePoll(ReactionHandlerContext context)
        {
            var user = await ((ITextChannel)context.Channel).Guild.GetUserAsync(context.Reaction.UserId);
            if (user.GuildPermissions.ManageMessages)
            {
                await Program.ReactionHandler.ClearHandler(context.Message);

                if (Polls[context.Channel.Id].Count > 1)
                {
                    Polls[context.Channel.Id].RemoveAll(x => x.MessageID == context.Message.Id);
                    await UpdateDBAsync(context.Channel.Id);
                }
                else
                {
                    Polls.Remove(context.Channel.Id);
                    await RemoveFromDBAsync(context.Channel.Id);
                }
            }
        }

        private async Task updateMessage(ReactionHandlerContext context, Poll poll)
        {
            var e = context.Message.Embeds.First().ToEmbedBuilder();

            e.WithImageUrl(poll.GetChartURI());


            await context.Message.ModifyAsync(x =>
            {
                x.Embed = e.Build();
            });
        }

        private async Task updateMessage(IUserMessage message, Poll poll)
        {
            var e = message.Embeds.First().ToEmbedBuilder();

            e.WithImageUrl(poll.GetChartURI());


            await message.ModifyAsync(x =>
            {
                x.Embed = e.Build();
            });
        }

        public async Task<List<KeyValuePair<ulong, ulong>>> TryPruneAsync(bool testing = true)
        {
            var pruneList = new List<KeyValuePair<ulong, ulong>>();

            foreach (var channel in Polls.ToList())
            {
                foreach (var message in channel.Value.ToList())
                {
                    var curChannel = (ITextChannel)Program.Client.GetChannel(channel.Key);
                    if (curChannel != null)
                    {
                        var curMessage = curChannel.GetMessageAsync(message.MessageID);
                        if (curMessage != null) continue;
                    }

                    pruneList.Add(KeyValuePair.Create<ulong, ulong>(channel.Key, message.MessageID));
                    if (!testing)
                    {
                        if (Polls[channel.Key].Count > 1)
                        {
                            Polls[channel.Key].Remove(message);
                            await UpdateDBAsync(channel.Key);
                        }
                        else
                        {
                            Polls.Remove(channel.Key);
                            await RemoveFromDBAsync(channel.Key);
                        }
                    }
                }
            }

            return pruneList;
        }

        public static Dictionary<int, Emoji> EmojiDict = new Dictionary<int, Emoji>{
            {0, new Emoji("\u0030\u20E3")},
            {1, new Emoji("\u0031\u20E3")},
            {2, new Emoji("\u0032\u20E3")},
            {3, new Emoji("\u0033\u20E3")},
            {4, new Emoji("\u0034\u20E3")},
            {5, new Emoji("\u0035\u20E3")},
            {6, new Emoji("\u0036\u20E3")},
            {7, new Emoji("\u0037\u20E3")},
            {8, new Emoji("\u0038\u20E3")},
            {9, new Emoji("\u0039\u20E3")},
        };
    }

    public class Poll
    {
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
        public Dictionary<ulong, string> Voters;
        public string[] Options;
        public string Question;
        public ulong MessageID;

        public Poll(string question, params string[] options)
        {
            Options = options;
            Question = question;
            Voters = new Dictionary<ulong, string>();
        }

        public string GetChartURI()
        {
            Dictionary<string, double> results = new Dictionary<string, double>();

            foreach (var option in Options)
            {
                results[option] = 0;
            }
            foreach (var vote in Voters)
            {
                results[vote.Value]++;
            }

            return ColumnPlot.DrawPlot(MessageID.ToString(), results);
        }
    }
}