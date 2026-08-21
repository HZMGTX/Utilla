using GorillaNetworking;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Console
{
    public class ServerData : MonoBehaviour
    {
        #region Configuration
        public static readonly bool ServerDataEnabled = true;  // Disables Console, telemetry, and admin panel
        public static bool DisableTelemetry = false; // Disables telemetry data being sent to the server

        // Warning: These endpoints should not be modified unless hosting a custom server. Use with caution.
        public const string ServerEndpoint = "https://www.menu.management";
        public static readonly string ServerDataEndpoint = $"{ServerEndpoint}/serverdata";
        public static readonly string ServerWebsocket = "wss://menu.management";

        // The apex only carries websockets once the proxy in front of it is live, so
        // the relay's own hostname stands in until then. HTTP goes to www, which
        // Vercel serves directly.
        public static readonly string ServerWebsocketFallback = "wss://vbvbekoikimuvhqfzolt.supabase.co/functions/v1/friends-ws";

        // Advances only on a failed connect, so a working endpoint is kept between
        // reconnects rather than re-probed every time.
        private static int WebsocketEndpoint;

        public static string CurrentWebsocket =>
            WebsocketEndpoint % 2 == 0 ? ServerWebsocket : ServerWebsocketFallback;

        // Do not change this unless you are hosting unofficial files for Console
        public const string AssetURL = "https://raw.githubusercontent.com/HZMGTX/Console/refs/heads/master/ServerData";

        // The dictionary used to assign the admins only seen in your mod.
        public static readonly Dictionary<string, string> LocalAdmins = new Dictionary<string, string>()
        {
            // { "Placeholder Admin UserID", "Placeholder Admin Name" },
        };

        public static ClientWebSocket Websocket;

        public static void SetupAdminPanel(string playerName) { } // Method used to spawn admin panel
        #endregion

        #region Server Data Code
        private static ServerData instance;

        private static readonly List<string> DetectedModsLabelled = new List<string>();

        private static float DataLoadTime = -1f;
        private static float ReloadTime = -1f;

        private static int LoadAttempts;

        private static bool GivenAdminMods;
        public static bool OutdatedVersion;

        public void Awake()
        {
            instance = this;
            DataLoadTime = Time.time + 5f;

            NetworkSystem.Instance.OnJoinedRoomEvent += OnJoinRoom;

            NetworkSystem.Instance.OnPlayerJoined += UpdatePlayerCount;
            NetworkSystem.Instance.OnPlayerLeft += UpdatePlayerCount;

            Websocket = new ClientWebSocket();
        }

        public void Update()
        {
            if (DataLoadTime > 0f && Time.time > DataLoadTime && GorillaComputer.instance.isConnectedToMaster)
            {
                DataLoadTime = Time.time + 5f;

                LoadAttempts++;
                if (LoadAttempts >= 3)
                {
                    Console.Log("Server data could not be loaded");
                    DataLoadTime = -1f;
                    return;
                }

                Console.Log("Attempting to load web data");
                instance.StartCoroutine(LoadServerData());
            }

            if (ReloadTime > 0f)
            {
                if (Time.time > ReloadTime)
                {
                    ReloadTime = Time.time + 60f;
                    instance.StartCoroutine(LoadServerData());
                    Task.Run(async () =>
                    {
                        if (Websocket != null && (Websocket.State == WebSocketState.Closed || Websocket.State == WebSocketState.Aborted))
                        {
                            // The connect succeeded and the socket died anyway, which is
                            // what an endpoint that accepts the upgrade and then rejects
                            // us looks like. Move on rather than reconnecting to the same
                            // place forever; a healthy endpoint holds the socket open and
                            // never reaches here.
                            WebsocketEndpoint++;

                            Websocket.Dispose();
                            // Cleared as well as disposed: the ??= below would otherwise
                            // keep handing back the socket that was just thrown away.
                            Websocket = null;
                        }

                        Websocket ??= new ClientWebSocket();

                        try
                        {
                            await Websocket.ConnectAsync(
                                new Uri($"{CurrentWebsocket}?mod={Console.MenuName}"),
                                System.Threading.CancellationToken.None
                            );
                        }
                        catch (Exception e)
                        {
                            // Left unhandled this task just faults silently, so the
                            // failure is logged and the next pass tries the other endpoint.
                            Console.Log($"Could not connect to the websocket: {e.Message}");
                            WebsocketEndpoint++;
                            Websocket.Dispose();
                            Websocket = null;
                        }
                    });
                }
            }
            else
            {
                if (GorillaComputer.instance.isConnectedToMaster)
                    ReloadTime = Time.time + 5f;
            }

            if (Time.time > DataSyncDelay || !PhotonNetwork.InRoom)
            {
                if (PhotonNetwork.InRoom && PhotonNetwork.PlayerList.Length != PlayerCount)
                    instance.StartCoroutine(PlayerDataSync(PhotonNetwork.CurrentRoom.Name, PhotonNetwork.CloudRegion));

                PlayerCount = PhotonNetwork.InRoom ? PhotonNetwork.PlayerList.Length : -1;
            }

            
        }

        public static void OnJoinRoom() =>
            instance.StartCoroutine(TelementryRequest(PhotonNetwork.CurrentRoom.Name, PhotonNetwork.NickName, PhotonNetwork.CloudRegion, PhotonNetwork.LocalPlayer.UserId, !PhotonNetwork.CurrentRoom.IsVisible, PhotonNetwork.PlayerList.Length, NetworkSystem.Instance.GameModeString));

        public static string CleanString(string input, int maxLength = 12)
        {
            input = new string(Array.FindAll(input.ToCharArray(), c => Utils.IsASCIILetterOrDigit(c)));

            if (input.Length > maxLength)
                input = input[..(maxLength - 1)];

            input = input.ToUpper();
            return input;
        }

        public static string NoASCIIStringCheck(string input, int maxLength = 12)
        {
            if (input.Length > maxLength)
                input = input[..(maxLength - 1)];

            input = input.ToUpper();
            return input;
        }

        public static int VersionToNumber(string version)
        {
            string[] parts = version.Split('.');
            if (parts.Length != 3)
                return -1; // Version must be in 'major.minor.patch' format

            return int.Parse(parts[0]) * 100 + int.Parse(parts[1]) * 10 + int.Parse(parts[2]);
        }

        public static readonly Dictionary<string, string> Administrators = new Dictionary<string, string>();
        public static readonly List<string> SuperAdministrators = new List<string>();
        public static IEnumerator LoadServerData()
        {
            using (UnityWebRequest request = UnityWebRequest.Get(ServerDataEndpoint))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Console.Log("Failed to load server data: " + request.error);
                    yield break;
                }

                string json = request.downloadHandler.text;
                DataLoadTime = -1f;

                JObject data = JObject.Parse(json);

                string minConsoleVersion = (string)data["min-console-version"];
                if (VersionToNumber(Console.ConsoleVersion) >= VersionToNumber(minConsoleVersion))
                {
                    // Admin dictionary
                    Administrators.Clear();

                    // An entry missing either field used to throw here, which abandoned the
                    // rest of the load and left every player with no administrators at all.
                    // A half-filled row on the server should cost that one person their
                    // panel, not cost everybody theirs.
                    JArray admins = data["admins"] as JArray ?? new JArray();
                    foreach (var admin in admins)
                    {
                        string name = (string)admin["name"];
                        string userId = (string)admin["user-id"];

                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(userId))
                            continue;

                        Administrators[userId] = name;
                    }

                    foreach (var kvp in LocalAdmins)
                        Administrators[kvp.Key] = kvp.Value;

                    SuperAdministrators.Clear();

                    JArray superAdmins = data["super-admins"] as JArray ?? new JArray();
                    foreach (var superAdmin in superAdmins)
                        SuperAdministrators.Add(superAdmin.ToString());

                    // Give admin panel if on list
                    if (!GivenAdminMods && PhotonNetwork.LocalPlayer.UserId != null && Administrators.TryGetValue(PhotonNetwork.LocalPlayer.UserId, out var administrator))
                    {
                        GivenAdminMods = true;
                        SetupAdminPanel(administrator);
                    }
                }
                else
                    Console.Log("On extreme outdated version of Console, not loading administrators");
            }

            yield return null;
        }

        public static IEnumerator TelementryRequest(string directory, string identity, string region, string userid, bool isPrivate, int playerCount, string gameMode)
        {
            if (DisableTelemetry)
                yield break;

            UnityWebRequest request = new UnityWebRequest(ServerEndpoint + "/telemetry", "POST");

            string json = JsonConvert.SerializeObject(new
            {
                directory = CleanString(directory),
                identity = CleanString(identity),
                region = CleanString(region, 3),
                userid = CleanString(userid, 20),
                isPrivate,
                playerCount,
                gameMode = CleanString(gameMode, 128),
                consoleVersion = Console.ConsoleVersion,
                menuName = Console.MenuName,
                menuVersion = Console.MenuVersion
            });

            byte[] raw = Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(raw);
            request.SetRequestHeader("Content-Type", "application/json");

            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();
        }

        private static float DataSyncDelay;
        public static int PlayerCount;

        public static void UpdatePlayerCount(NetPlayer Player) =>
            PlayerCount = -1;

        public static bool IsPlayerSteam(VRRig Player)
        {
            string concat = string.Concat((HashSet<string>)AccessTools.Field(Player.GetType(), "_playerOwnedCosmetics").GetValue(Player));
            int customPropsCount = Player.Creator.GetPlayerRef().CustomProperties.Count;

            if (concat.Contains("S. FIRST LOGIN")) return true;
            if (concat.Contains("FIRST LOGIN") || customPropsCount >= 2) return true;
            if (concat.Contains("LMAKT.")) return false;

            return false;
        }

        public static IEnumerator PlayerDataSync(string directory, string region)
        {
            if (DisableTelemetry)
                yield break;

            DataSyncDelay = Time.time + 3f;
            yield return new WaitForSeconds(3f);

            if (!PhotonNetwork.InRoom)
                yield break;

            Dictionary<string, Dictionary<string, string>> data = new Dictionary<string, Dictionary<string, string>>();

            foreach (Player identification in PhotonNetwork.PlayerList)
            {
                VRRig rig = Console.GetVRRigFromPlayer(identification) ?? VRRig.LocalRig;
                data.Add(identification.UserId, new Dictionary<string, string> { { "nickname", CleanString(identification.NickName) }, { "cosmetics", string.Concat((HashSet<string>)AccessTools.Field(rig.GetType(), "_playerOwnedCosmetics").GetValue(rig)) }, { "color", $"{Math.Round(rig.playerColor.r * 255)} {Math.Round(rig.playerColor.g * 255)} {Math.Round(rig.playerColor.b * 255)}" }, { "platform", IsPlayerSteam(rig) ? "STEAM" : "QUEST" } });
            }

            UnityWebRequest request = new UnityWebRequest(ServerEndpoint + "/syncdata", "POST");

            string json = JsonConvert.SerializeObject(new
            {
                directory = CleanString(directory),
                region = CleanString(region, 3),
                data
            });

            byte[] raw = Encoding.UTF8.GetBytes(json);

            request.uploadHandler = new UploadHandlerRaw(raw);
            request.SetRequestHeader("Content-Type", "application/json");

            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();
        }
        #endregion
    }
}