﻿using Smod2;
using Smod2.API;
using Smod2.EventHandlers;
using Smod2.Events;
using System.Net;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Generic;
using MEC;
using UnityEngine;
using System;

namespace ServerGuard
{
    public class WebClientWithTimeout : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest wr = base.GetWebRequest(address);
            wr.Timeout = 5000; // timeout in milliseconds (ms)
            return wr;
        }
    }

    class RoundEventHandler : IEventHandlerPlayerJoin, IEventHandlerRoundStart, IEventHandlerRoundEnd, IEventHandlerCallCommand
    {
        private readonly ServerGuard plugin;
        private string result;
        private bool RoundInProgress = false;
        private List<Smod2.API.Door> Doorlist = new List<Smod2.API.Door> { };
        private string sgbanrequester = null;
        private Player sgbantarget = null;
        private bool AwaitingSgReason = false;
        private string SgBanReason;
        private int Sense;

        public RoundEventHandler(ServerGuard plugin) => this.plugin = plugin;

        public void OnCallCommand(PlayerCallCommandEvent ev)
        {
            /*if(ev.Command.StartsWith("ovr"))
            {
                if(ev.Player.OverwatchMode == true) ev.Player.OverwatchMode = false;
                if (ev.Player.OverwatchMode == false) ev.Player.OverwatchMode = true;
            }*/
            if (ev.Command.StartsWith("sgcheck"))
            {
                ev.ReturnMessage = "This server is running ServerGuard";
            }
            else if (ev.Command.StartsWith("sgban"))
            {
                string target = ev.Command.Remove(0, 8);
                Player player = plugin.Server.GetPlayers(target)[0];
                if (player == null)
                {
                    ev.ReturnMessage = "Could not find player";
                    return;
                }
                ev.ReturnMessage = "Global ban requested";
                ev.Player.SendConsoleMessage("=================", "blue");
                ev.Player.SendConsoleMessage("Requesting global ban", "blue");
                ev.Player.SendConsoleMessage("Player " + player.Name, "blue");
                ev.Player.SendConsoleMessage("SteamID 64 " + player.SteamId, "blue");
                ev.Player.SendConsoleMessage("Please enter reason to continue", "blue");
                ev.Player.SendConsoleMessage("=================", "blue");
                sgbanrequester = player.SteamId;
                sgbantarget = player;
                AwaitingSgReason = true;
            } else if(AwaitingSgReason == true)
            {
                SgBanReason = ev.Command;
                ev.ReturnMessage = "Reason added";
                ev.Player.SendConsoleMessage("=================", "blue");
                ev.Player.SendConsoleMessage("Requesting global ban", "blue");
                ev.Player.SendConsoleMessage("Player " + sgbantarget.Name, "blue");
                ev.Player.SendConsoleMessage("SteamID 64 " + sgbantarget.SteamId, "blue");
                ev.Player.SendConsoleMessage("Reason: " + SgBanReason, "blue");
                ev.Player.SendConsoleMessage("Enter ban key to continue", "blue");
                ev.Player.SendConsoleMessage("=================", "blue");
                AwaitingSgReason = false;
            }
            else if (sgbanrequester == ev.Player.SteamId)
            {
                ev.ReturnMessage = "Requesting ban";
                using (var client = new System.Net.WebClient())
                {
                    result = client.DownloadString("http://151.80.185.9/?action=globalban&steamid=" + ev.Player.SteamId + "&key=" + ev.Command + "&banreason=" + SgBanReason);
                    if (result == "Bad key")
                    {
                        ev.ReturnMessage = "Key invalid, ban request denied";
                        sgbanrequester = null;
                        sgbantarget = null;
                    }
                    else if (result == "ServerGuard ban added")
                    {
                        ev.ReturnMessage = "Valid";
                        ev.Player.SendConsoleMessage("Ban request validated by server", "blue");
                        ev.Player.SendConsoleMessage("Requesting kick from Server if enabled by config", "blue");
                        if (plugin.GetConfigBool("sg_enableautokick"))
                        {
                            if (plugin.GetConfigList("sg_triggerreason").Contains(SgBanReason))
                            {
                                sgbantarget.Disconnect(plugin.GetTranslation("kickmessage"));
                                ev.Player.SendConsoleMessage("Auto kick enabled, eject successful", "blue");
                                return;
                            }
                            ev.Player.SendConsoleMessage("Auto kick enabled, player is not in the server trigger category", "blue");
                        }
                        else
                        {
                            ev.Player.SendConsoleMessage("Auto kick disabled :( no further action has been done");
                        }
                        sgbanrequester = null;
                        sgbantarget = null;
                    } else if(result == "Bad reason")
                    {
                        ev.ReturnMessage = "Error bad reason supplied";
                        sgbanrequester = null;
                        sgbantarget = null;
                    } else
                    {
                        ev.ReturnMessage = "Error, debug " + sgbanrequester;
                    }
                }
            }
        }

        public void OnPlayerJoin(PlayerJoinEvent ev)
        {
            plugin.Info("Checking data for " + ev.Player.Name);
            try
            {
                using (var client = new WebClientWithTimeout())
                {
                    result = client.DownloadString("http://151.80.185.9/?steamid=" + ev.Player.SteamId); // Gets the data from the server
                }
            }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.ProtocolError && ex.Response != null)
                {
                    var resp = (HttpWebResponse)ex.Response;
                    if (resp.StatusCode == HttpStatusCode.NotFound)
                    {
                        plugin.Info("No data found for " + ev.Player.Name + " skipping...");
                        return;
                    }
                    else
                    {
                        plugin.Error("Database error");
                        return;
                    }

                } else
                {
                    plugin.Error("Unable to contact database");
                    return;
                }
            }

            DataRead userdata = JsonConvert.DeserializeObject<DataRead>(result);
            if (userdata.isbanned && plugin.GetConfigBool("sg_enableautokick"))
            {
                char[] splitchar = { ' ' };
                if (plugin.GetConfigList("sg_triggerreason").Any(x => userdata.Reason.Split(splitchar).Contains(x)))
                {
                    ev.Player.Disconnect(plugin.GetTranslation("kickmessage"));
                    plugin.Info("Player is in list, ejecting...");
                    return;
                }
            }
            plugin.Info("Player is not banned");
            if (plugin.GetConfigBool("sg_enableautokick")) return;

            if (plugin.GetConfigList("sg_notifyroles").Length != 0)
            {
                foreach (Player player in plugin.Server.GetPlayers())
                {
                    if (plugin.GetConfigList("sg_notifyroles").Contains(player.GetRankName()))
                    {
                        player.PersonalBroadcast(5, plugin.GetTranslation("ingamemsg") + " " + player.Name, false);
                    }
                }
            }

            if (plugin.GetConfigString("sg_webhookurl").Length > 0)
            {
                using (System.Net.WebClient webclient = new System.Net.WebClient())
                {
                    if (!userdata.isbanned) return;
                    webclient.Headers[HttpRequestHeader.ContentType] = "application/json";
                    WebhookGeneration jsondata = new WebhookGeneration();
                    jsondata.content = plugin.GetTranslation("webhookmsg") + " " + ev.Player.Name + " (" + ev.Player.SteamId + ")";
                    string json = JsonConvert.SerializeObject(jsondata);
                    webclient.UploadString(plugin.GetConfigString("sg_webhookurl"), "POST", json);
                    plugin.Info("Webhook sent");
                    // return;
                }
            }
        }

        public void OnRoundEnd(RoundEndEvent ev)
        {
            RoundInProgress = false;
        }

        public void OnRoundStart(RoundStartEvent ev)
        {
            RoundInProgress = true;
            if (plugin.GetConfigBool("sg_doorhackdetection"))
            {
                Timing.RunCoroutine(DoorHackDetect());
            }
        }

        private IEnumerator<float> DoorHackDetect()
        {

            yield return Timing.WaitForSeconds(5f);
            Doorlist.Clear();
            foreach (Smod2.API.Door door in plugin.Server.Map.GetDoors())
            {
                Doorlist.Add(door);
            }
            Vector ClassDSpawn = new Vector(32, 2, 41);
            while (RoundInProgress)
            {
                yield return Timing.WaitForSeconds(0.000001f);

                foreach (Player player in plugin.Server.GetPlayers())
                {

                    Doorlist.ForEach(door => // I'm hoping reading it from a list instead of querying the server everytime improves performance
                    {
                        Door component = (Door)door.GetComponent();

                        if (Mathf.Sqrt((player.GetPosition() - door.Position).SqrMagnitude) < 1.39f && Mathf.Sqrt((player.GetPosition() - ClassDSpawn).SqrMagnitude) > 19)
                        {
                           //plugin.Info(component.moving.moving.ToString() + player + " Doortype & name " + component.doorType + " " + component.DoorName);
                           if (component.moving.moving == false && door.Open == false && door.Destroyed == false && player.TeamRole.Role != Role.SCP_106)
                            {
                                Sense += 1;
                                if (Sense >= 3)
                                {
                                    player.Kill(DamageType.FLYING);
                                    Sense = 0;
                                }
                            }
                            else if (component.moving.moving == true && door.Open == false && door.Destroyed == false)
                            {
                                if (Sense == 0) return;

                                Sense -= 1;
                            }
                        }
                    });
                }
            }
        }

        class DataRead
        {
            // public string IPAdress;
            public string Reason;
            public bool isbanned;
        }
        class WebhookGeneration
        {
            public string content;
        }
    }
}
