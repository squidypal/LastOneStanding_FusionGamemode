using BoneLib.BoneMenu.Elements;
using LabFusion.MarrowIntegration;
using LabFusion.Network;
using LabFusion.Representation;
using LabFusion.SDK.Points;
using LabFusion.Senders;
using LabFusion.Utilities;
using System.Collections.Generic;
using System.Reflection;
using LabFusion.SDK.Gamemodes;
using UnityEngine;
using MelonLoader;
using SwipezGamemodeLib.Spectator;
using SwipezGamemodeLib.Utilities;

namespace LastOneStanding
{
    public class Class1 : MelonMod
    {
        public override void OnInitializeMelon()
        {
            // Required to register your gamemode with fusion
            GamemodeRegistration.LoadGamemodes(Assembly.GetExecutingAssembly());
        }
    }
    public class LastOneStanding : Gamemode {
        public static LastOneStanding Instance { get; private set; }

        // This gamemode specific stuff: 
        // Default value for death tax and int used for prize tax
        private const int _defaultVal = 20;
        private int prizeTotal;
        private int _val = _defaultVal;
        // Used later for a list of player IDs
        private List<PlayerId> players;
        
        // Name of gamemode, and the category it should be under (in Bonemenu)
        public override string GamemodeCategory => "Last One Standing";
        public override string GamemodeName => "Last One Standing";
        
        // Disabling things to stop cheaters from cheating
        public override bool DisableDevTools => true;
        public override bool DisableSpawnGun => true;
        public override bool DisableManualUnragdoll => true;
        
        // Whether or not players can join mid-game 
        public override bool PreventNewJoins => !_enabledLateJoining;
        private bool _enabledLateJoining = true;
        
        // Used later to balance players
        private bool _hasOverridenValues = false;
        private string _avatarOverride = null;
        private float? _vitalityOverride = null;

        // Check when BoneMenu is available to be added to in-game
        public override void OnBoneMenuCreated(MenuCategory category) {
            // Create BoneMenu elements
            base.OnBoneMenuCreated(category);

            // Creating a new Bonemenu element, that allows players to change the death tax
            category.CreateIntElement("Death tax (Can be set to 0)", Color.green, _val, 10, 0, 1000, (v) => {
                _val = v;
            });
        }
        
        // On MetaDataChanged can be called with a key and value, this can be used to send data to the server
        // The "key" is simply a way of differentiating what info you want to send
        // You can see how to send MetaData in the OnStartGamemode class, where I use it to send the info of the prize pool to the server
         protected override void OnMetadataChanged(string key, string value)
        {
            if (key == "DeathTax")
            {
                int.TryParse(value, out _val);
                // Calculate prize pool
                int playerCount = PlayerIdManager.PlayerCount - 1;
                prizeTotal = _val * playerCount;
                
                FusionNotifier.Send(new FusionNotification()                                  
                {                                                                             
                    // Send notification with details on prize pool                           
                    title = "Prize Pool",                                                     
                    showTitleOnPopup = true,                                                  
                    message = "The prize pool is " + prizeTotal + "! " + _val + " per player!",    
                    isMenuItem = false,                                                       
                    isPopup = true,                                                           
                    popupLength = 3f,                                                         
                });   
            }
        }

         // On player action uses a switch for different cases of player actions
         // You can check with PlayerActionType. JUMP, DEATH, DYING, UNKNOWN (idk what this is), RECOVERY, DEATH_BY_OTHER_PLAYER
         // Additionally you can check the ID of the player that died, and the ID of the player that killed them if using DEATH_BY_OTHER_PLAYER
        protected void OnPlayerAction(PlayerId player, PlayerActionType type, PlayerId otherPlayer) {
            if (IsActive())
            {
                switch (type)
                {
                    case PlayerActionType.DEATH:
                        MakePlayerSpectator(player);
                        // Remove player ID from list of players
                        players.Remove(player);
                        if (player == PlayerIdManager.LocalId)
                        {
                            // Remove bits from player
                            PointItemManager.DecrementBits(_val);
                            // Tell player they're now spectating, and how many bits they lost
                            FusionNotifier.Send(new FusionNotification()                                    
                            {
                                title = "YOU DIED!",                                                       
                                showTitleOnPopup = true,                                                    
                                message = "You are now spectating! " + _val + " bits deducted!", 
                                popupLength = 3f,                                                           
                                isMenuItem = false,                                                         
                                isPopup = true,                                                             
                            });     
                        }
                        else
                        {
                            // Announce player death, and number of players left to other players
                            string name;
                            player.TryGetDisplayName(out name);
                            // Send a fusion notification
                            FusionNotifier.Send(new FusionNotification()
                            {
                                // <color> allows you to change the colour of the text (requires swipe's gamemode lib)
                                title = name + "<color=#FF0000> died!",
                                showTitleOnPopup = true,
                                message = players.Count + " players left!",
                                isMenuItem = false,
                                isPopup = true,
                                popupLength = 1.5f,
                            });
                        }

                        break;
                }
            }
        }

        private void MakePlayerSpectator(PlayerId playerId)
        {
            if (playerId == PlayerIdManager.LocalId)
            {
                // Make player spectator when they respawn
                FusionPlayer.SetAmmo(0);
                FusionPlayerExtended.SetWorldInteractable(false);
                FusionPlayerExtended.SetCanDamageOthers(false);
                FusionPlayer.SetPlayerVitality(1000);
            }
            else
            {
                playerId.Hide();
            }
        }

        // Ran whenever a player leaves the game
        protected void OnPlayerLeave(PlayerId playerId)
        {
            // If gamemode is active and the player is in the list of players, remove them from the list
            if (IsActive())
            {
                players.Remove(playerId);
                MakePrizePool();
            }
        }

        // Ran whenever a player joins the game
        protected void OnPlayerJoin(PlayerId playerId)
        {
            // If gamemode is active and a player joins, add them to the list of players
            if (IsActive())
            {
                MakePlayerSpectator(playerId);
                if (playerId == PlayerIdManager.LocalId)
                {
                    FusionNotifier.Send(new FusionNotification()
                    {
                        title = "YOU ARE SPECTATING",
                        showTitleOnPopup = true,
                        message = "You will stop spectating once the round ends",
                        isMenuItem = false,
                        isPopup = true,
                        popupLength = 3f,
                    });
                }
            }
        }

        // Ran every frame
        protected override void OnUpdate()
        {
            // Check if there is only one player left
            if (!IsActive()) return;
            // If there is only one player still alive, end the game 
            if (players.Count <= 1)
            {
                StopGamemode();
            }
        }

        private void MakePrizePool()
        {
            // Check if player is the server owner, and if they are send the information on the DeathTax
            if (NetworkInfo.IsServer)
            {
                // "DeathTax" is the key which is used to recognize the message in the OnMetaDataChanged function
                TrySetMetadata("DeathTax", _val.ToString());
            }
        }
        
        // Runs when the gamemode is started
        protected override void OnStartGamemode()
        {
            base.OnStartGamemode();
            
            // Run the MakePrizePool function
            MakePrizePool();
            
            // Set all player health to 1 
            FusionPlayer.SetPlayerVitality(1);
            
            // Make a list of players 
            players = new List<PlayerId>();
            players.AddRange(PlayerIdManager.PlayerIds);

            // Send match started notification
            FusionNotifier.Send(new FusionNotification()
            {
                title = "Match Started!",
                showTitleOnPopup = true,
                message = "Be the last one standing!",
                isMenuItem = false,
                isPopup = true,
            });

            // PVP Stuffs
            // Run these changes when the level is loaded
            FusionSceneManager.HookOnLevelLoad(() => {
                // Force player to be mortal
                FusionPlayer.SetMortality(true);
                // Give player ammo 
                FusionPlayer.SetAmmo(10000);
                // Get all the spawnpoints
                List<Transform> transforms = new List<Transform>();
                // Steal the deathmatch spawns :trollface:
                foreach (var point in DeathmatchSpawnpoint.Cache.Components) {
                    transforms.Add(point.transform);
                }
                FusionPlayer.SetSpawnPoints(transforms.ToArray());
                
                // Telelport the player to a random spawn point
                if (FusionPlayer.TryGetSpawnPoint(out var spawn)) {
                    FusionPlayer.Teleport(spawn.position, spawn.forward);
                }
                
                // Nametag updates
                FusionOverrides.ForceUpdateOverrides();

                // Balance the avatars by applying the vitality and overriding stats
                if (_avatarOverride != null)
                    FusionPlayer.SetAvatarOverride(_avatarOverride);

                if (_vitalityOverride.HasValue)
                    FusionPlayer.SetPlayerVitality(_vitalityOverride.Value);
            });
        }
        
        // Runs when gamemode ends or is stopped 
        protected override void OnStopGamemode()
        {
            base.OnStopGamemode();
            // If game ended correctly (not stopped by host)
            if (players.Count <= 1)
            {
                // Get winner and send notification     
                
                // Get winner's name
                string name;
                players[0].TryGetDisplayName(out name);
                
                // Check if winner is local player
                if (players[0] == PlayerIdManager.LocalId)
                {
                    // If winner is local player, award local player bits
                    PointItemManager.RewardBits(prizeTotal);
                }
                // Send notification with details on winner   
                FusionNotifier.Send(new FusionNotification()
                {
                    title = "Game Over!",
                    showTitleOnPopup = true,
                    message = "The winner is " + name + "! They won " + prizeTotal + " bits!",
                    popupLength = 3f,
                    isMenuItem = false,
                    isPopup = true,
                });
            }
            else
            {
                // Send a different notification if game ended early
                FusionNotifier.Send(new FusionNotification()
                {
                    title = "Game Over!",
                    showTitleOnPopup = true,
                    message = "Game ended early! No one won!",
                    popupLength = 3f,
                    isMenuItem = false,
                    isPopup = true,
                });
            }

            // Make spectators visible
            // Probably a nice bit of code to just copy and paste if you're using spectators
            foreach (var t in PlayerIdManager.PlayerIds)
            {
                t.Show();
            }
            if (!FusionPlayerExtended.worldInteractable)
            {
                // If local player is not world interactive, make them world interactive 
                FusionPlayerExtended.SetWorldInteractable(true);
                // Refresh player's health to normal
                FusionPlayer.ClearPlayerVitality();
                FusionPlayerExtended.SetCanDamageOthers(true);
            }


            // Reset game back to sandbox 
            FusionPlayer.ResetMortality();
            
            FusionPlayer.SetAmmo(100000);
            
            FusionPlayer.ResetSpawnPoints();
            
            FusionOverrides.ForceUpdateOverrides();
            
            FusionPlayer.ClearAvatarOverride();
            FusionPlayer.ClearPlayerVitality();
        }
        
        // For PVP gamemodes I would recommend just copy and pasting most if not all of this code
        public override void OnMainSceneInitialized() {
            if (!_hasOverridenValues) {
                SetDefaultValues();
            }
            else {
                _hasOverridenValues = false;
            }
        }

        public override void OnLoadingBegin() {
            _hasOverridenValues = false;
        }

        public void SetDefaultValues() {
            // Steal the deathmatch music x3
            SetPlaylist(DefaultMusicVolume, FusionContentLoader.CombatPlaylist);
            
            _avatarOverride = null;
            _vitalityOverride = null;

            // Whether or not players should be able to join mid-game
            _enabledLateJoining = true;
        }
        
        public override void OnGamemodeRegistered() {
            Instance = this;
            
            // Add fusion hooks
            MultiplayerHooking.OnPlayerAction += OnPlayerAction;
            FusionOverrides.OnValidateNametag += OnValidateNametag;
            MultiplayerHooking.OnPlayerLeave += OnPlayerLeave;
            MultiplayerHooking.OnPlayerJoin += OnPlayerJoin;

            // Run the SetDefaultValues function
            SetDefaultValues();
        }

        public override void OnGamemodeUnregistered() {
            if (Instance == this)
                Instance = null;
            
            // Remove fusion hooks when no longer needed
            MultiplayerHooking.OnPlayerAction -= OnPlayerAction;
            FusionOverrides.OnValidateNametag -= OnValidateNametag;
            MultiplayerHooking.OnPlayerLeave -= OnPlayerLeave;
            MultiplayerHooking.OnPlayerJoin -= OnPlayerJoin;
        }

        protected bool OnValidateNametag(PlayerId id) {
            if (!IsActive())
                return true;

            return false;
        }
    }
}