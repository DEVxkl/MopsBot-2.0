﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Discord.Commands;
using Discord.WebSocket;
using MopsBot.Module.Preconditions;
using System.IO;
using static MopsBot.StaticBase;

namespace MopsBot
{
    public class CommandHandler
    {
        private CommandService commands;
        private DiscordSocketClient client;
        private IServiceProvider _provider;

        /// <summary>
        /// Add command/module Service and create Events
        /// </summary>
        /// <param name="provider">The Service Provider</param>
        /// <returns>A Task that can be awaited</returns>
        public async Task Install(IServiceProvider provider)
        {
            // Create Command Service, inject it into Dependency Map
            _provider = provider;
            client = _provider.GetService<DiscordSocketClient>();

            commands = new CommandService();
            //_map.Add(commands);

            await commands.AddModulesAsync(Assembly.GetEntryAssembly());

            guildPrefix = new Dictionary<ulong, string>();
            fillPrefix();
            client.MessageReceived += Client_MessageReceived;
            client.MessageReceived += HandleCommand;
            client.UserJoined += Client_UserJoined;
            client.UserLeft += Client_UserLeft;
        }

        /// <summary>
        /// Manages polls and experience gain whenever a message is recieved
        /// Also keeps track of how many characters have been sent each day
        /// </summary>
        /// <param name="arg">The recieved message</param>
        /// <returns>A Task that can be awaited</returns>
        private async Task Client_MessageReceived(SocketMessage arg)
        {
            //Poll
            if (arg.Channel is Discord.IDMChannel && StaticBase.poll != null)
            {
                if (StaticBase.poll.participants.ToList().Select(x => x.Id).Contains(arg.Author.Id))
                {
                    StaticBase.poll.AddValue(StaticBase.poll.answers[int.Parse(arg.Content) - 1], arg.Author.Id);
                    await arg.Channel.SendMessageAsync("Vote accepted!");
                    StaticBase.poll.participants.RemoveAll(x => x.Id == arg.Author.Id);
                }
            }

            //Daily Statistics & User Experience
            if (!arg.Author.IsBot && !arg.Content.StartsWith("!"))
            {
                StaticBase.people.AddStat(arg.Author.Id, arg.Content.Length, "experience");
                StaticBase.stats.AddValue(arg.Content.Length);
            }
        }

        /// <summary>
        /// Greets User when he joins a Guild
        /// </summary>
        /// <param name="User">The User who joined</param>
        /// <returns>A Task that can be awaited</returns>
        private async Task Client_UserJoined(SocketGuildUser User)
        {
            //PhunkRoyalServer Begruessung
            if (User.Guild.Id.Equals(205130885337448469))
                await User.Guild.GetTextChannel(305443055396192267).SendMessageAsync($"Willkommen im **{User.Guild.Name}** Server, {User.Mention}!" +
                $"\n\nBevor Du vollen Zugriff auf den Server hast, möchten wir Dich auf die Regeln des Servers hinweisen, die Du hier findest:" +
                $" {User.Guild.GetTextChannel(305443033296535552).Mention}\nSobald Du fertig bist, kannst Du Dich an einen unserer Moderatoren zu Deiner" +
                $" rechten wenden, die Dich alsbald zum Mitglied ernennen.\n\nHave a very mopsig day\nDein heimlicher Verehrer Mops");
            await StaticBase.UpdateGameAsync();
        }

        private async Task Client_UserLeft(SocketGuildUser User)
        {
            await StaticBase.UpdateGameAsync();
        }

        /// <summary>
        /// Checks if message is a command, and executes it
        /// </summary>
        /// <param name="parameterMessage">The message to check</param>
        /// <returns>A Task that can be awaited</returns>
        public async Task HandleCommand(SocketMessage parameterMessage)
        {
            // Don't handle the command if it is a system message
            var message = parameterMessage as SocketUserMessage;
            if (message == null) return;

            // Mark where the prefix ends and the command begins
            int argPos = 0;

            //Determines if the Guild has set a special prefix, if not, ! is used
            ulong id = 0;
            if(message.Channel is Discord.IDMChannel) id = message.Channel.Id;
            else id = ((SocketGuildChannel)message.Channel).Guild.Id;
            var prefix = guildPrefix.ContainsKey(id) ? guildPrefix[id] : "!";

            // Determine if the message has a valid prefix, adjust argPos 
            if (!(message.HasMentionPrefix(client.CurrentUser, ref argPos) || message.HasStringPrefix(prefix, ref argPos) || message.HasCharPrefix('?', ref argPos))) return;

            StaticBase.people.AddStat(parameterMessage.Author.Id, 0, "experience");

            if (message.Content.Contains("help") || message.HasCharPrefix('?', ref argPos))
            {
                await getCommands(parameterMessage, prefix);
                return;
            }

            if (char.IsWhiteSpace(message.Content[argPos]))
                argPos += 1;

            // Create a Command Context
            var context = new CommandContext(client, message);
            // Execute the Command, store the result
            var result = await commands.ExecuteAsync(context, argPos, _provider);

            // If the command failed, notify the user
            if (!result.IsSuccess && !result.ErrorReason.Contains("Unknown command")){
                await message.Channel.SendMessageAsync($"**Error:** {result.ErrorReason}");
            }
        }

        /// <summary>
        /// Creates help message as well as command information, and sends it
        /// </summary>
        /// <param name="msg">The message recieved</param>
        /// <returns>A Task that can be awaited</returns>
        public async Task getCommands(SocketMessage msg, string prefix)
        {
            string output = "";

            if (msg.Content.Contains("help"))
            {
                output += "For more information regarding a specific command, please use ?<command>";

                foreach (var module in commands.Modules)
                {
                    if (module.IsSubmodule)
                    {
                        output += $"`{module.Name}*` ";
                    }
                    else
                    {
                        output += $"\n**{module.Name}**: ";
                        foreach (var command in module.Commands)
                            if (!command.Preconditions.OfType<HideAttribute>().Any())
                                output += $"`{command.Name}` ";
                    }
                }
            }

            else
            {
                string message = msg.Content.Replace("?", "").ToLower();

                string commandName = message, moduleName = message;

                string[] tempMessages;
                if ((tempMessages = message.Split(' ')).Length > 1)
                {
                    moduleName = tempMessages[0];
                    commandName = tempMessages[1];
                }

                if (commands.Commands.ToList().Exists(x => x.Name.ToLower().Equals(commandName)))
                {
                    CommandInfo curCommand = commands.Commands.First(x => x.Name.ToLower().Equals(commandName));
                    if (!moduleName.Equals(commandName))
                    {
                        curCommand = commands.Modules.First(x => x.Name.ToLower().Equals(moduleName)).Commands.First(x => x.Name.ToLower().Equals(commandName));
                    }

                    output += $"`{curCommand.Name}`:\n";
                    output += curCommand.Summary;
                    output += $"\n\n**Usage**: `{prefix}{(curCommand.Module.IsSubmodule ? curCommand.Module.Name + " " + curCommand.Name : curCommand.Name)}";
                    foreach (Discord.Commands.ParameterInfo p in curCommand.Parameters)
                    {
                        output += $" <{p.Name}>";
                    }
                    output += "`";
                }
                else
                {
                    ModuleInfo curModule = commands.Modules.First(x => x.Name.ToLower().Equals(moduleName));

                    output += $"**{curModule.Name}**:";

                    foreach (CommandInfo curCommand in curModule.Commands)
                        output += $" `{curCommand.Name}`";
                }
            }

            await msg.Channel.SendMessageAsync(output);
        }

        private void fillPrefix()
        {
            string s = "";
            using (StreamReader read = new StreamReader(new FileStream("mopsdata//guildprefixes.txt", FileMode.OpenOrCreate)))
            {
                while ((s = read.ReadLine()) != null)
                {
                    try
                    {
                        var trackerInformation = s.Split('|');
                        var prefix = trackerInformation[1];
                        var guildID = ulong.Parse(trackerInformation[0]);
                        if (!guildPrefix.ContainsKey(guildID))
                        {
                            guildPrefix.Add(guildID, prefix);
                        }

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
            }
        }
    }
}
