﻿using System;
using System.Linq;
using System.Threading;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;
using Shared;
using static Shared.CommonEnumerators;

namespace GameClient
{
    public static class TransferManager
    {
        public static void ParseTransferPacket(Packet packet)
        {
            TransferManifestJSON transferManifestJSON = (TransferManifestJSON)Serializer.ConvertBytesToObject(packet.contents);

            switch (int.Parse(transferManifestJSON.transferStepMode))
            {
                //settlement recieves request
                case (int)TransferStepMode.TradeRequest:
                    ReceiveTransferRequest(transferManifestJSON);
                    break;

                //Caravan's trade is accepted
                case (int)TransferStepMode.TradeAccept:
                    DialogManager.PopDialog();
                    DialogManager.PushNewDialog(new RT_Dialog_OK("Transfer was a success!"));
                    if (int.Parse(transferManifestJSON.transferMode) == (int)TransferMode.Pod) LaunchDropPods();
                    FinishTransfer(true);
                    break;

                case (int)TransferStepMode.TradeReject:
                    DialogManager.PopDialog();
                    DialogManager.PushNewDialog(new RT_Dialog_Error("Player rejected the trade!", DialogManager.PopDialog));
                    RecoverTradeItems(TransferLocation.Caravan);
                    break;

                case (int)TransferStepMode.TradeReRequest:
                    DialogManager.PopDialog();
                    ReceiveReboundRequest(transferManifestJSON);
                    break;

                case (int)TransferStepMode.TradeReAccept:
                    DialogManager.PopDialog();
                    GetTransferedItemsToSettlement(DeepScribeManager.GetAllTransferedItems(ClientValues.incomingManifest));
                    break;

                case (int)TransferStepMode.TradeReReject:
                    DialogManager.PopDialog();
                    DialogManager.PushNewDialog(new RT_Dialog_Error("Player rejected the trade!", DialogManager.PopDialog));
                    RecoverTradeItems(TransferLocation.Settlement);
                    break;

                case (int)TransferStepMode.Recover:
                    DialogManager.PopDialog();
                    RecoverTradeItems(TransferLocation.Caravan);
                    break;
            }
        }

        public static void TakeTransferItems(CommonEnumerators.TransferLocation transferLocation)
        {
            ClientValues.outgoingManifest.fromTile = Find.AnyPlayerHomeMap.Tile.ToString();

            if (transferLocation == CommonEnumerators.TransferLocation.Caravan) ClientValues.outgoingManifest.toTile = ClientValues.chosenSettlement.Tile.ToString();
            else if (transferLocation == CommonEnumerators.TransferLocation.Settlement) ClientValues.outgoingManifest.toTile = ClientValues.incomingManifest.fromTile.ToString();

            if (TradeSession.deal.TryExecute(out bool actuallyTraded))
            {
                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();

                if (transferLocation == CommonEnumerators.TransferLocation.Caravan)
                {
                    TradeSession.playerNegotiator.GetCaravan().RecacheImmobilizedNow();
                }
            }
        }

        public static void TakeTransferItemsFromPods(CompLaunchable representative)
        {
            ClientValues.outgoingManifest.transferMode = ((int)TransferMode.Pod).ToString();
            ClientValues.outgoingManifest.fromTile = Find.AnyPlayerHomeMap.Tile.ToString();
            ClientValues.outgoingManifest.toTile = ClientValues.chosenSettlement.Tile.ToString();

            foreach (CompTransporter pod in representative.TransportersInGroup)
            {
                ThingOwner directlyHeldThings = pod.GetDirectlyHeldThings();

                for(int i = 0; i < directlyHeldThings.Count(); i++)
                {
                    TransferManagerHelper.AddThingToTransferManifest(directlyHeldThings[i], directlyHeldThings[i].stackCount);
                }
            }
        }

        public static void SendTransferRequestToServer(TransferLocation transferLocation)
        {
            DialogManager.PushNewDialog(new RT_Dialog_Wait("Waiting for transfer response"));

            Logs.Message($"There are {ClientValues.outgoingManifest.itemDetailsJSONS.Count()} items being traded");

            if (transferLocation == TransferLocation.Caravan)
            {
                ClientValues.outgoingManifest.transferStepMode = ((int)TransferStepMode.TradeRequest).ToString();

                Packet packet = Packet.CreatePacketFromJSON("TransferPacket", ClientValues.outgoingManifest);
                Network.listener.dataQueue.Enqueue(packet);
            }

            else if (transferLocation == TransferLocation.Settlement)
            {
                ClientValues.outgoingManifest.transferStepMode = ((int)TransferStepMode.TradeReRequest).ToString();

                Packet packet = Packet.CreatePacketFromJSON("TransferPacket", ClientValues.outgoingManifest);
                Network.listener.dataQueue.Enqueue(packet);
            }

            else if (transferLocation == TransferLocation.Pod)
            {
                ClientValues.outgoingManifest.transferStepMode = ((int)TransferStepMode.TradeRequest).ToString();

                Packet packet = Packet.CreatePacketFromJSON("TransferPacket", ClientValues.outgoingManifest);
                Network.listener.dataQueue.Enqueue(packet);
            }
        }

        public static void RecoverTradeItems(TransferLocation transferLocation)
        {
            try
            {
                Action r1 = delegate
                {
                    Thing[] toRecover = DeepScribeManager.GetAllTransferedItems(ClientValues.outgoingManifest);

                    if (transferLocation == TransferLocation.Caravan)
                    {
                        GetTransferedItemsToCaravan(toRecover, false);
                    }

                    else if (transferLocation == TransferLocation.Settlement)
                    {
                        GetTransferedItemsToSettlement(toRecover, false);
                    }

                    else if (transferLocation == TransferLocation.Pod)
                    {
                        //Do nothing
                    }
                };
                r1.Invoke();
            }

            catch
            {
                Logs.Warning("Rethrowing transfer items, might be Rimworld's fault");

                Thread.Sleep(100);

                RecoverTradeItems(transferLocation);
            }
        }

        public static void GetTransferedItemsToSettlement(Thing[] things, bool success = true, bool customMap = true, bool invokeMessage = true)
        {
            Action r1 = delegate
            {
                Map map = null;
                if (customMap) map = Find.Maps.Find(x => x.Tile == int.Parse(ClientValues.incomingManifest.toTile));
                else map = Find.AnyPlayerHomeMap;

                IntVec3 location = GetTransferLocationInMap(map);

                foreach (Thing thing in things)
                {
                    if (thing is Pawn) GenSpawn.Spawn(thing, location, map, Rot4.Random);
                    else GenPlace.TryPlaceThing(thing, location, map, ThingPlaceMode.Near);
                }

                FinishTransfer(success);
            };

            if (invokeMessage)
            {
                if (success) DialogManager.PushNewDialog(new RT_Dialog_OK("Transfer was a success!", r1));
                else DialogManager.PushNewDialog(new RT_Dialog_Error("Transfer was cancelled!", r1));
            }
            else r1.Invoke();
        }

        public static void GetTransferedItemsToCaravan(Thing[] things, bool success = true, bool invokeMessage = true)
        {
            Action r1 = delegate
            {
                foreach (Thing thing in things)
                {
                    if (TransferManagerHelper.CheckIfThingIsHuman(thing) || TransferManagerHelper.CheckIfThingIsAnimal(thing))
                    {
                        Find.WorldPawns.PassToWorld(thing as Pawn);
                        thing.SetFaction(Faction.OfPlayer);
                    }

                    ClientValues.chosenCaravan.AddPawnOrItem(thing, true);
                }

                FinishTransfer(success);
            };

            if (invokeMessage)
            {
                if (success) DialogManager.PushNewDialog(new RT_Dialog_OK("Transfer was a success!", r1));
                else DialogManager.PushNewDialog(new RT_Dialog_Error("Transfer was cancelled!", r1));
            }
            else r1.Invoke();
        }

        public static void FinishTransfer(bool success)
        {
            if (success) SaveManager.ForceSave();

            ClientValues.incomingManifest = new TransferManifestJSON();
            ClientValues.outgoingManifest = new TransferManifestJSON();
            ClientValues.ToggleTransfer(false);

            DialogManager.ClearStack();
        }

        public static void ReceiveTransferRequest(TransferManifestJSON transferManifestJSON)
        {
            try
            {
                ClientValues.incomingManifest = transferManifestJSON;

                if (!ClientValues.isReadyToPlay || ClientValues.isInTransfer || ClientValues.autoDenyTransfers)
                {
                    RejectRequest((TransferMode)int.Parse(transferManifestJSON.transferMode));
                }

                else
                {
                    Action r1 = delegate
                    {
                        if (int.Parse(transferManifestJSON.transferMode) == (int)TransferMode.Gift)
                        {
                            RT_Dialog_ItemListing d1 = new RT_Dialog_ItemListing(DeepScribeManager.GetAllTransferedItems(transferManifestJSON), TransferMode.Gift);
                            DialogManager.PushNewDialog(d1);
                        }

                        else if (int.Parse(transferManifestJSON.transferMode) == (int)TransferMode.Trade)
                        {
                            RT_Dialog_ItemListing d1 = new RT_Dialog_ItemListing(DeepScribeManager.GetAllTransferedItems(transferManifestJSON), TransferMode.Trade);
                            DialogManager.PushNewDialog(d1);
                        }

                        else if (int.Parse(transferManifestJSON.transferMode) == (int)TransferMode.Pod)
                        {
                            RT_Dialog_ItemListing d1 = new RT_Dialog_ItemListing(DeepScribeManager.GetAllTransferedItems(transferManifestJSON), TransferMode.Pod);
                            DialogManager.PushNewDialog(d1);
                        }
                    };

                    if (int.Parse(transferManifestJSON.transferMode) == (int)TransferMode.Gift)
                    {
                        DialogManager.PushNewDialog(new RT_Dialog_OK("You are receiving a gift request", r1));
                    }

                    else if (int.Parse(transferManifestJSON.transferMode) == (int)TransferMode.Trade)
                    {
                        DialogManager.PushNewDialog(new RT_Dialog_OK("You are receiving a trade request", r1));
                    }

                    else if (int.Parse(transferManifestJSON.transferMode) == (int)TransferMode.Pod)
                    {
                        DialogManager.PushNewDialog(new RT_Dialog_OK("You are receiving a gift request", r1));
                    }
                }
            }

            catch
            {
                Logs.Warning("Rethrowing transfer items, might be Rimworld's fault");

                Thread.Sleep(100);

                ReceiveTransferRequest(transferManifestJSON);
            }        
        }

        public static void ReceiveReboundRequest(TransferManifestJSON transferManifestJSON)
        {
            try
            {
                ClientValues.incomingManifest = transferManifestJSON;

                RT_Dialog_ItemListing d1 = new RT_Dialog_ItemListing(DeepScribeManager.GetAllTransferedItems(transferManifestJSON), TransferMode.Rebound);
                DialogManager.PushNewDialog(d1);
            }

            catch
            {
                Logs.Warning("Rethrowing transfer items, might be Rimworld's fault");

                Thread.Sleep(100);

                ReceiveReboundRequest(transferManifestJSON);
            }
        }

        public static void RejectRequest(TransferMode transferMode)
        {
            if (transferMode == TransferMode.Gift)
            {
                //Nothing should happen here
            }

            else if (transferMode == TransferMode.Trade)
            {
                ClientValues.incomingManifest.transferStepMode = ((int)TransferStepMode.TradeReject).ToString();

                Packet packet = Packet.CreatePacketFromJSON("TransferPacket", ClientValues.incomingManifest);
                Network.listener.dataQueue.Enqueue(packet);
            }

            else if (transferMode == TransferMode.Pod)
            {
                //Nothing should happen here
            }

            else if (transferMode == TransferMode.Rebound)
            {
                ClientValues.incomingManifest.transferStepMode = ((int)TransferStepMode.TradeReReject).ToString();

                Packet packet = Packet.CreatePacketFromJSON("TransferPacket", ClientValues.incomingManifest);
                Network.listener.dataQueue.Enqueue(packet);

                RecoverTradeItems(TransferLocation.Caravan);
            }

            FinishTransfer(false);
        }

        public static IntVec3 GetTransferLocationInMap(Map map)
        {
            Thing tradingSpot = map.listerThings.AllThings.Find(x => x.def.defName == "RTTransferSpot");
            if (tradingSpot != null) return tradingSpot.Position;
            else
            {
                RT_Dialog_OK_Loop d1 = new RT_Dialog_OK_Loop(new string[] { "You are missing a transfer spot!",
                    "Received items will appear in the center of the map",
                    "Build a trading spot to change the drop location!"});

                DialogManager.PushNewDialog(d1);

                return new IntVec3(map.Center.x, map.Center.y, map.Center.z);
            }
        }

        public static void SendSilverToCaravan(int quantity)
        {
            ItemDetailsJSON itemDetailsJSON = new ItemDetailsJSON();
            itemDetailsJSON.defName = ThingDefOf.Silver.defName;
            itemDetailsJSON.materialDefName = "null";
            itemDetailsJSON.quantity = quantity.ToString();
            itemDetailsJSON.quality = "1";
            itemDetailsJSON.hitpoints = "100";
            itemDetailsJSON.isMinified = false;

            Thing silverToRecover = ThingScribeManager.GetItemSimple(itemDetailsJSON);
            ClientValues.chosenCaravan.AddPawnOrItem(silverToRecover, false);
        }

        public static void LaunchDropPods()
        {
            ClientValues.chosendPods.TryLaunch(
                ClientValues.chosenSettlement.Tile, new TransportPodsArrivalAction_GiveGift(ClientValues.chosenSettlement));
        }
    }

    public static class TransferManagerHelper
    {
        public static bool CheckIfThingIsHuman(Thing thing)
        {
            if (thing.def.defName == "Human") return true;
            else return false;
        }

        public static bool CheckIfThingIsAnimal(Thing thing)
        {
            PawnKindDef animal = DefDatabase<PawnKindDef>.AllDefs.ToList().Find(fetch => fetch.defName == thing.def.defName);
            if (animal != null) return true;
            else return false;
        }

        public static bool CheckIfThingHasMaterial(Thing thing)
        {
            if (thing.Stuff != null) return true;
            else return false;
        }

        public static string GetThingQuality(Thing thing)
        {
            QualityCategory qc = QualityCategory.Normal;
            thing.TryGetQuality(out qc);

            return ((int)qc).ToString();
        }

        public static bool CheckIfThingIsMinified(Thing thing)
        {
            if (thing.def == ThingDefOf.MinifiedThing || thing.def == ThingDefOf.MinifiedTree) return true;
            else return false;
        }

        public static void AddThingToTransferManifest(Thing thing, int thingCount)
        {
            if (CheckIfThingIsHuman(thing))
            {
                Pawn pawn = thing as Pawn;

                ClientValues.outgoingManifest.humanDetailsJSONS.Add(Serializer.SerializeToString
                    (HumanScribeManager.TransformHumanToString(pawn, false)));
            }

            else if (CheckIfThingIsAnimal(thing))
            {
                Pawn pawn = thing as Pawn;

                ClientValues.outgoingManifest.animalDetailsJSON.Add(Serializer.SerializeToString
                    (AnimalScribeManager.TransformAnimalToString(pawn)));
            }

            else
            {
                ClientValues.outgoingManifest.itemDetailsJSONS.Add(Serializer.SerializeToString
                    (ThingScribeManager.TransformItemToString(thing, thingCount)));
            }
        }
    }
}
