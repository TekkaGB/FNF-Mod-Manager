﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FNF_Mod_Manager
{
    public class Mod
    {
        public string name { get; set; }
        public bool enabled { get; set; }
    }
    public class Metadata
    {
        public Uri preview { get; set; }
        public string submitter { get; set; }
        public Uri avi { get; set; }
        public Uri upic { get; set; }
        public Uri caticon { get; set; }
        public string cat { get; set; }
        public string section { get; set; }
        public string description { get; set; }
        public Uri homepage { get; set; }
        public DateTime? lastupdate { get; set; }
    }
    public class Config
    {
        public string exe { get; set; }
        public List<string> exes { get; set; }
        public ObservableCollection<Mod> ModList { get; set; }
    }
}
