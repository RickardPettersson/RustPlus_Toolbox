using System;
using System.Collections.Generic;
using System.Text;

namespace RustPlus_Toolbox.Models
{
    public class ServerItem
    {
        public int Id { get; set; }
        public bool Active { get; set; }
        public string Name { get; set; }
        public string RustPlusConfigPath { get; set; }
        public string ServerIP { get; set; }
        public int RustPlusPort { get; set; }
        public ulong SteamId { get; set; }
        public int PlayerToken { get; set; }
        public float BaseLocationX { get; set; }
        public float BaseLocationY { get; set; }
        public float Radius { get; set; }
        public List<ServerItemEntity> Entities { get; set; } = new List<ServerItemEntity>();
    }

    public class ServerItemEntity
    {
        public uint EntityId { get; set; }
        public int EntityType { get; set; } // 1 = Smart Switch, 2 = Smart Alarm, 3 = Storage Monitor
        public bool? State { get; set; } = null;
        public string Name { get; set; }
    }
}
