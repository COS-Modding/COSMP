using COSML;
using COSML.Components.Toast;
using COSML.Modding;
using COSMP.Network;
using COSMP.Network.Client;
using COSMP.Network.Core;
using COSMP.Network.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static COSML.MainMenu.MenuUtils;

namespace COSMP
{
    public class COSMP : Mod, IModMenu, IGlobalSettings<GlobalData>
    {
        public COSMP() : base("COSMP") { }
        public override string GetVersion() => "alpha-1";
        public void OnLoadGlobal(GlobalData data) => globalData = data;
        public GlobalData OnSaveGlobal() => globalData;

        internal static COSMP Instance;
        internal static Dictionary<PlayerCanvasAction, Sprite> CanvasActions;

        private GlobalData globalData = new();

        private NetworkManager manager;

        private MenuButton hostButton;
        private MenuButton joinButton;
        private MenuText connectingText;
        private MenuButton leaveButton;
        private MenuButton connectedMenu;
        private MenuButton bannedMenu;
        private readonly List<MenuOption> playersListOptions = [];
        private readonly List<MenuOption> bannedListOptions = [];

        public override void Init()
        {
            Instance = this;

            hostButton = new(new I18nKey("cosmp.menu.host"), onClick: Host);
            joinButton = new(new I18nKey("cosmp.menu.join"), onClick: Join);
            connectingText = new(new I18nKey("cosmp.menu.connecting"), visible: false);
            leaveButton = new(new I18nKey("cosmp.menu.leave"), onClick: Leave, visible: false);
            connectedMenu = new MenuButton(new I18nKey("cosmp.menu.connected", 1), new MenuMain("PlayersList", new I18nKey("cosmp.menu.players.list"), playersListOptions), visible: false);
            bannedMenu = CreateBannedMenu();

            ModHooks.ApplicationQuitHook += Leave;

            LoadEmbedded();
        }

        private void SetServerHost(string value)
        {
            globalData.host = value;
        }

        private void SetServerPort(string value)
        {
            try
            {
                globalData.port = int.Parse(value);
            }
            catch (Exception)
            {
                Warn($"Cannot convert '{value}' to int");
            }
        }

        private void SetUsername(string value)
        {
            globalData.username = value.Substring(0, Math.Min(COSMPConstants.USERNAME_MAX_LENGTH, value.Length)).Trim();
        }

        private void Host()
        {
            if (manager != null)
            {
                if (manager.IsConnected()) return;
                manager.Stop();
            }

            if (IsUsernameEmpty(globalData.username))
            {
                Toast.Show(new I18nKey("cosmp.username.empty"), 3);
                Warn("Invalid username");
                return;
            }

            try
            {
                Server server = new(globalData);
                manager = server;
                server.JoinHook += OnJoin;
                server.LeaveHook += OnLeave;
                server.PingHook += OnPing;
                server.PositionHook += OnPosition;
            }
            catch (Exception ex)
            {
                Error(ex);
                Toast.Show(new I18nKey("cosmp.error.create.server"), 3);
            }
            RefreshButtons();
        }

        private void Join()
        {
            if (manager != null)
            {
                if (manager.IsConnected()) return;
                manager.Stop();
            }

            if (IsUsernameEmpty(globalData.username))
            {
                Toast.Show(new I18nKey("cosmp.username.empty"), 3);
                Warn("Invalid username");
                return;
            }

            try
            {
                Client client = new(globalData);
                manager = client;
                client.LoginHook += OnLogin;
                client.JoinHook += OnJoin;
                client.LeaveHook += OnLeave;
                client.DisconnectHook += OnDisconnect;
                client.MetaHook += RefreshPlayersList;
                client.PositionHook += OnPosition;
            }
            catch (Exception ex)
            {
                Error(ex);
                Toast.Show(new I18nKey("cosmp.error.create.client"), 3);
            }

            RefreshButtons();
        }

        private void OnLogin()
        {
            Toast.Show(new I18nKey("cosmp.self.join"), 3);
            RefreshButtons();
        }

        private void OnJoin(PlayerData data)
        {
            Toast.Show(new I18nKey("cosmp.client.join", data.Username), 3);
            RefreshPlayersList();
        }

        private void OnLeave(PlayerData data)
        {
            Toast.Show(new I18nKey("cosmp.client.leave", data.Username), 3);
            RefreshPlayersList();
        }

        private void OnPing(PlayerData data)
        {
            UpdatePlayerButton(data);
        }

        private void OnPosition(PlayerData data)
        {
            RefreshPlayersList();
        }

        private void OnDisconnect(string reason, NetworkErrorCode code)
        {
            I18nKey key = new("cosmp.error.unknown", reason);
            switch (code)
            {
                case NetworkErrorCode.Connect:
                    key = new("cosmp.error.connect", reason);
                    break;

                case NetworkErrorCode.Disconnect:
                    key = new("cosmp.error.disconnect", reason);
                    break;
            }
            Toast.Show(key);
            Leave();
        }

        private void Leave()
        {
            manager?.Stop();
            manager = null;

            RefreshButtons();
        }

        private void RefreshButtons()
        {
            hostButton.SetVisible(manager == null);
            joinButton.SetVisible(manager == null);

            bool connected = manager != null && manager.IsConnected();
            leaveButton.SetVisible(connected);
            connectedMenu.SetVisible(connected);

            bool connecting = manager != null && !manager.IsConnected();
            connectingText.SetVisible(connecting);

            RefreshPlayersList();
        }

        private void RefreshPlayersList()
        {
            PlayerData[] players = manager?.Players ?? [];
            connectedMenu.SetLabel(new I18nKey("cosmp.menu.connected", Math.Max(players.Length, 1)));

            playersListOptions.Clear();
            playersListOptions.AddRange(players.Select(CreatePlayerButton));

            bannedMenu.SetVisible(globalData.banned.Count > 0);
            bannedListOptions.Clear();
            bannedListOptions.AddRange(globalData.banned.Select(CreateBannedButton));
        }

        private MenuOption CreatePlayerButton(PlayerData data)
        {
            List<MenuOption> optionsList = [];
            if (data.Id != manager.Data.Id && manager is Server server)
            {
                return new MenuButton(
                        GetPlayerButtonLabel(data),
                        new MenuMain(
                            $"Player-{data.Id}",
                            data.Username,
                            [
                                new MenuButton(
                                    new I18nKey("cosmp.menu.kick"),
                                    onClick: () =>
                                    {
                                        if (server.Kick(data.Id))
                                        {
                                            RefreshPlayersList();
                                            GoTo("Menu_Mods_COSMP");
                                            Refresh();
                                        }
                                    }),
                                new MenuButton(
                                    new I18nKey("cosmp.menu.ban"),
                                    onClick: () =>
                                    {
                                        if (server.Ban(data.Id))
                                        {
                                            RefreshPlayersList();
                                            GoTo("Menu_Mods_COSMP");
                                            Refresh();
                                        }
                                    })
                            ]
                        )
                    );
            }

            return new MenuText(GetPlayerButtonLabel(data));
        }

        private void UpdatePlayerButton(PlayerData data)
        {
            if (playersListOptions.Count <= 0) return;

            int index = Array.FindIndex(manager.Players, d => d.Id.Equals(data.Id));
            if (index < 0) return;

            playersListOptions[index].SetLabel(GetPlayerButtonLabel(data));
        }

        private MenuButton CreateBannedButton(string ip)
        {
            return new MenuButton(
                ip,
                new MenuMain(
                    $"Banned-{ip}",
                    ip,
                    [
                        new MenuButton(
                            new I18nKey("cosmp.menu.unban"),
                            onClick: () => {
                                globalData.banned.Remove(ip);
                                RefreshPlayersList();
                                GoTo("Menu_Mods_COSMP");
                                Refresh();
                            }
                        )
                    ]
                )
            );
        }

        private string GetPlayerButtonLabel(PlayerData data)
        {
            return $"{data.Username} [{data.Ping}ms]{(data.Position.Place != null ? $"- {data.Position.Place}" : "")}";
        }

        private bool IsUsernameEmpty(string value) => string.IsNullOrWhiteSpace(value);

        private MenuButton CreateBannedMenu()
        {
            return new MenuButton(
                new I18nKey("cosmp.menu.banned"),
                new MenuMain(
                    "BannedList",
                    new I18nKey("cosmp.menu.banned"),
                    bannedListOptions
                ),
                visible: false
            );
        }

        public IList<MenuOption> GetMenu() => [
            new MenuTextInput(new I18nKey("cosmp.menu.username"), globalData.username, 20, onInput: SetUsername),
            new MenuTextInput(new I18nKey("cosmp.menu.address"), globalData.host, onInput: SetServerHost),
            new MenuTextInput(new I18nKey("cosmp.menu.port"), globalData.port.ToString(), onInput: SetServerPort),
            hostButton,
            joinButton,
            connectingText,
            leaveButton,
            connectedMenu,
            bannedMenu
        ];

        private void LoadEmbedded()
        {
            CanvasActions = [];
            CanvasActions.Add(PlayerCanvasAction.Pause, Assembly.GetExecutingAssembly().LoadEmbeddedSprite("COSMP.Resources.action_pause.png", 128f));
            CanvasActions.Add(PlayerCanvasAction.Journal, Assembly.GetExecutingAssembly().LoadEmbeddedSprite("COSMP.Resources.action_journal.png", 128f));
            CanvasActions.Add(PlayerCanvasAction.Inventory, Assembly.GetExecutingAssembly().LoadEmbeddedSprite("COSMP.Resources.action_inventory.png", 128f));
        }
    }
}
