﻿using RimWorld;
using RimworldTogether.GameClient.Dialogs;
using RimworldTogether.GameClient.Managers.Actions;
using RimworldTogether.GameClient.Misc;
using RimworldTogether.GameClient.Patches;
using RimworldTogether.GameClient.Values;
using RimworldTogether.Shared.JSON;
using RimworldTogether.Shared.Network;
using System;
using RimworldTogether.Shared.Misc;
using RimworldTogether.Shared.Serializers;
using Shared.Misc;

namespace RimworldTogether.GameClient.Managers
{
    public static class CommandManager
    {
        public static void ParseCommand(Packet packet)
        {
            CommandDetailsJSON commandDetailsJSON = (CommandDetailsJSON)ObjectConverter.ConvertBytesToObject(packet.contents);

            switch(int.Parse(commandDetailsJSON.commandType))
            {
                case (int)CommonEnumerators.CommandType.Op:
                    OnOpCommand();
                    break;

                case (int)CommonEnumerators.CommandType.Deop:
                    OnDeopCommand();
                    break;

                case (int)CommonEnumerators.CommandType.Ban:
                    OnBanCommand();
                    break;

                case (int)CommonEnumerators.CommandType.Disconnect:
                    PersistentPatches.DisconnectToMenu();
                    break;

                case (int)CommonEnumerators.CommandType.Quit:
                    PersistentPatches.QuitGame();
                    break;

                case (int)CommonEnumerators.CommandType.Broadcast:
                    OnBroadcastCommand(commandDetailsJSON);
                    break;

                case (int)CommonEnumerators.CommandType.ForceSave:
                    OnForceSaveCommand();
                    break;
            }
        }

        private static void OnOpCommand()
        {
            ServerValues.isAdmin = true;
            PersistentPatches.ManageDevOptions();
            DialogManager.PushNewDialog(new RT_Dialog_OK("You are now an admin!"));
        }

        private static void OnDeopCommand()
        {
            ServerValues.isAdmin = false;
            PersistentPatches.ManageDevOptions();
            DialogManager.PushNewDialog(new RT_Dialog_OK("You are no longer an admin!"));
        }

        private static void OnBanCommand()
        {
            DialogManager.PushNewDialog(new RT_Dialog_OK("You have been banned from the server!"));
        }

        private static void OnBroadcastCommand(CommandDetailsJSON commandDetailsJSON)
        {
            LetterManager.GenerateLetter("Server Broadcast", commandDetailsJSON.commandDetails, LetterDefOf.PositiveEvent);
        }

        private static void OnForceSaveCommand()
        {
            if (!ClientValues.isReadyToPlay) PersistentPatches.DisconnectToMenu();
            else
            {
                ClientValues.isDisconnecting = true;
                SavePatch.ForceSave();
            }
        }
    }
}
