﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameServer
{
    public class ChatCommand
    {
        public string prefix;

        public string description;

        public int parameters;

        public Action commandAction;

        public ChatCommand(string prefix, int parameters, string description, Action commandAction)
        {
            this.prefix = prefix;
            this.parameters = parameters;
            this.description = description;
            this.commandAction = commandAction;
        }
    }

    public class ServerCommand
    {
        public string prefix;

        public string description;

        public int parameters;

        public Action commandAction;

        public ServerCommand(string prefix, int parameters, string description, Action commandAction)
        {
            this.prefix = prefix;
            this.parameters = parameters;
            this.description = description;
            this.commandAction = commandAction;
        }
    }
}
