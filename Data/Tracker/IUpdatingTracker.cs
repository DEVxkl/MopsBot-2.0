using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
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
    public abstract class IUpdatingTracker : ITracker
    {
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
        public Dictionary<ulong, ulong> ToUpdate;

        public IUpdatingTracker(int interval, int gap = 5000) : base(interval, gap){
            ToUpdate = new Dictionary<ulong, ulong>();
        }

        public async virtual Task setReaction(IUserMessage message){}
    }
}