using HarmonyLib;
using Reptile;
using SlopCrew.Common;
using SlopCrew.Common.Network;
using SlopCrew.Common.Network.Clientbound;
using SlopCrew.Common.Network.Serverbound;
using SlopCrew.Plugin.Scripts;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Vector3 = System.Numerics.Vector3;

namespace SlopCrew.Plugin;

public class PlayerManager : IDisposable {
    public int CurrentOutfit = 0;
    public bool IsHelloRefreshQueued = false;
    public bool IsVisualRefreshQueued = false;
    public bool IsResetQueued = false;

    public bool IsPlayingAnimation = false;
    public bool IsSettingVisual = false;

    public Dictionary<uint, AssociatedPlayer> Players = new();
    public List<AssociatedPlayer> AssociatedPlayers => this.Players.Values.ToList();

    private Queue<NetworkSerializable> messageQueue = new();
    private float updateTick = 0;
    private int? lastAnimation;
    private Vector3 lastPos = Vector3.Zero;
    private Vector3 lastRot = Vector3.Zero;
    private bool stopAnnounced = false;

    private int scoreUpdateCooldown = 10;
    private (int, int) lastScoreAndMultiplier = (0, 0);

    public PlayerManager() {
        Core.OnUpdate += this.Update;
        StageManager.OnStageInitialized += this.StageInit;
        StageManager.OnStagePostInitialization += this.StagePostInit;
        Plugin.NetworkConnection.OnMessageReceived += this.OnMessage;
    }

    public void Reset() {
        this.CurrentOutfit = 0;
        this.IsHelloRefreshQueued = false;
        this.IsVisualRefreshQueued = false;
        this.IsResetQueued = false;
        this.IsPlayingAnimation = false;
        this.IsSettingVisual = false;

        this.Players.Values.ToList().ForEach(x => x.FuckingObliterate());
        this.Players.Clear();
        this.messageQueue.Clear();

        this.updateTick = 0;
        this.lastAnimation = null;
        this.lastPos = Vector3.Zero;
    }

    public void Dispose() {
        Core.OnUpdate -= this.Update;
        StageManager.OnStageInitialized -= this.StageInit;
        StageManager.OnStagePostInitialization -= this.StagePostInit;
        Plugin.NetworkConnection.OnMessageReceived -= this.OnMessage;
    }

    public AssociatedPlayer? GetAssociatedPlayer(Reptile.Player reptilePlayer) {
        return this.AssociatedPlayers.FirstOrDefault(x => x.ReptilePlayer == reptilePlayer);
    }

    private void StageInit() {
        this.Players.Clear();
    }

    private void StagePostInit() {
        this.IsHelloRefreshQueued = true;
    }

    public void Update() {
        if (this.IsResetQueued) {
            this.IsResetQueued = false;
            this.Reset();
            return;
        }

        var me = WorldHandler.instance?.GetCurrentPlayer();
        if (me is null) return;
        var traverse = Traverse.Create(me);

        var dt = Time.deltaTime;
        this.updateTick += dt;

        if (this.updateTick <= Constants.TickRate) return;
        this.updateTick = 0;

        this.HandlePositionUpdate(me);
        this.ProcessMessageQueue();
        this.HandleHelloRefresh(me, traverse);
        this.HandleVisualRefresh(me, traverse);
        this.UpdatePlayerCount();

        // Score can be spammed (e.g. doing a manual), send it less often
        this.scoreUpdateCooldown--;
        if (this.scoreUpdateCooldown <= 0) {
            this.scoreUpdateCooldown = 10;
            this.UpdateScore();
        }
    }

    private void HandlePositionUpdate(Reptile.Player me) {
        var position = me.transform.position;
        var rotation = me.transform.rotation.eulerAngles;
        var deltaMove = position.FromMentalDeficiency() - this.lastPos;
        var deltaRot = rotation.FromMentalDeficiency() - this.lastRot;
        var moved = (Math.Abs(deltaMove.Length()) > 0.125) || (Math.Abs(deltaRot.Length()) > 0.125);

        if (moved) {
            this.stopAnnounced = false;
            this.lastPos = position.FromMentalDeficiency();
            this.lastRot = rotation.FromMentalDeficiency();

            Plugin.NetworkConnection.SendMessage(new ServerboundPositionUpdate {
                Transform = new Common.Transform {
                    Position = position.FromMentalDeficiency(),
                    Rotation = me.transform.rotation.FromMentalDeficiency(),
                    Velocity = me.motor.velocity.FromMentalDeficiency(),
                    Stopped = false,
                    Tick = Plugin.NetworkConnection.ServerTick,
                    Latency = Plugin.NetworkConnection.ServerLatency
                }
            });
        } else if (!this.stopAnnounced) {
            this.stopAnnounced = true;

            Plugin.NetworkConnection.SendMessage(new ServerboundPositionUpdate {
                Transform = new Common.Transform {
                    Position = position.FromMentalDeficiency(),
                    Rotation = me.transform.rotation.FromMentalDeficiency(),
                    Velocity = Vector3.Zero,
                    Stopped = true,
                    Tick = Plugin.NetworkConnection.ServerTick,
                    Latency = Plugin.NetworkConnection.ServerLatency
                }
            });
        }
    }

    private void ProcessMessageQueue() {
        while (this.messageQueue.Count > 0) {
            var msg = this.messageQueue.Dequeue();
            this.OnMessageInternal(msg);
        }
    }

    private void HandleHelloRefresh(Reptile.Player me, Traverse traverse) {
        if (!this.IsHelloRefreshQueued) return;

        this.IsHelloRefreshQueued = false;
        var character = traverse.Field<Characters>("character").Value;
        var moveStyle = traverse.Field<MoveStyle>("moveStyle").Value;

        Plugin.NetworkConnection.SendMessage(new ServerboundPlayerHello {
            Player = new() {
                Name = Plugin.SlopConfig.Username.Value,
                ID = 1337, // filled in by the server; could be an int instead of uint but i'd have to change types everywhere

                Stage = (int) Core.Instance.BaseModule.CurrentStage,
                Character = (int) character,
                Outfit = this.CurrentOutfit,
                MoveStyle = (int) moveStyle,

                Transform = new Common.Transform {
                    Position = me.transform.position.FromMentalDeficiency(),
                    Rotation = me.transform.rotation.FromMentalDeficiency(),
                    Velocity = me.motor.velocity.FromMentalDeficiency(),
                    Stopped = false,
                    Tick = Plugin.NetworkConnection.ServerTick,
                    Latency = Plugin.NetworkConnection.ServerLatency
                },

                IsDeveloper = false
            },

            SecretCode = Plugin.SlopConfig.SecretCode.Value
        });
    }

    private void HandleVisualRefresh(Reptile.Player me, Traverse traverse) {
        if (!this.IsVisualRefreshQueued) return;

        this.IsVisualRefreshQueued = false;
        var characterVisual = traverse.Field<CharacterVisual>("characterVisual").Value;

        Plugin.NetworkConnection.SendMessage(new ServerboundVisualUpdate {
            BoostpackEffect = (int) characterVisual.boostpackEffectMode,
            FrictionEffect = (int) characterVisual.frictionEffectMode,
            Spraycan = characterVisual.VFX.spraycan.activeSelf,
            Phone = characterVisual.VFX.phone.activeSelf
        });
    }

    private void UpdatePlayerCount() {
        // +1 to include the current player
        Plugin.API.UpdatePlayerCount(this.Players.Count + 1);
    }

    private void UpdateScore() {
        var player = WorldHandler.instance?.GetCurrentPlayer();
        if (player is null || !player.isActiveAndEnabled) return;

        var traverse = Traverse.Create(player);
        var score = (int) traverse.Field<float>("baseScore").Value;
        var multiplier = (int) traverse.Field<float>("scoreMultiplier").Value;
        if (score == this.lastScoreAndMultiplier.Item1 && multiplier == this.lastScoreAndMultiplier.Item2) return;

        Plugin.NetworkConnection.SendMessage(new ServerboundScoreUpdate {
            Score = score,
            Multiplier = multiplier
        });
        this.lastScoreAndMultiplier = (score, multiplier);
    }

    private void OnMessage(NetworkSerializable msg) {
        this.messageQueue.Enqueue(msg);
    }

    private void OnMessageInternal(NetworkSerializable msg) {
        switch (msg) {
            case ClientboundPlayerAnimation playerAnimation:
                this.HandlePlayerAnimation(playerAnimation);
                break;

            case ClientboundPlayerPositionUpdate playerPositionUpdate:
                this.HandlePlayerPositionUpdate(playerPositionUpdate);
                break;

            case ClientboundPlayerScoreUpdate playerScoreUpdate:
                this.HandlePlayerScoreUpdate(playerScoreUpdate);
                break;

            case ClientboundPlayersUpdate playersUpdate:
                this.HandlePlayersUpdate(playersUpdate);
                break;

            case ClientboundPlayerVisualUpdate playerVisualUpdate:
                this.HandlePlayerVisualUpdate(playerVisualUpdate);
                break;
            case ClientboundRequestRace clientboundRequestRace:
                this.HandleRequestRace(clientboundRequestRace);
                break;
            case ClientboundRaceInitialize _:
                this.HandleRaceInitialize(); //TODO: May be sent the race id to match if we got the correct msg
                break;
            case ClientboundRaceStart _:
                this.HandleRaceStart();
                break;
            case ClientboundRaceRank clientboundRaceRank:
                this.HandleRaceRank(clientboundRaceRank);
                break;
        }
    }

    private void HandlePlayerAnimation(ClientboundPlayerAnimation playerAnimation) {
        if (this.Players.TryGetValue(playerAnimation.Player, out var associatedPlayer) &&
            associatedPlayer.ReptilePlayer is not null) {
            this.IsPlayingAnimation = true;
            associatedPlayer.ReptilePlayer.PlayAnim(
                playerAnimation.Animation,
                playerAnimation.ForceOverwrite,
                playerAnimation.Instant,
                playerAnimation.AtTime
            );
            this.IsPlayingAnimation = false;
        }
    }

    private void HandlePlayersUpdate(ClientboundPlayersUpdate playersUpdate) {
        var newPlayerIds = new HashSet<uint>(playersUpdate.Players.Select(p => p.ID));

        foreach (var player in playersUpdate.Players) {
            if (!this.Players.TryGetValue(player.ID, out var associatedPlayer)) {
                // New player
                this.Players[player.ID] = new AssociatedPlayer(player);
            } else {
                UpdateAssociatedPlayerIfDifferent(associatedPlayer, player);
            }
        }

        foreach (var currentPlayerId in this.Players.Keys.ToList()) {
            if (!newPlayerIds.Contains(currentPlayerId) &&
                this.Players.TryGetValue(currentPlayerId, out var associatedPlayer)) {
                associatedPlayer.FuckingObliterate();
                this.Players.Remove(currentPlayerId);
            }
        }
    }

    private void UpdateAssociatedPlayerIfDifferent(AssociatedPlayer associatedPlayer, Common.Player player) {
        var oldPlayer = associatedPlayer.SlopPlayer;
        var reptilePlayer = associatedPlayer.ReptilePlayer;

        // TODO: this kinda sucks
        var differentCharacter = oldPlayer.Character != player.Character;
        var differentOutfit = oldPlayer.Outfit != player.Outfit;
        var differentMoveStyle = oldPlayer.MoveStyle != player.MoveStyle;
        var isDifferent = differentCharacter || differentOutfit || differentMoveStyle;

        if (isDifferent) {
            //Plugin.Log.LogInfo("Updating associated player look");

            if (differentOutfit && !differentCharacter) {
                // Outfit-only requires a separate method
                reptilePlayer.SetOutfit(player.Outfit);
            } else if (differentCharacter || differentOutfit) {
                // New outfit
                reptilePlayer.SetCharacter((Characters) player.Character, player.Outfit);
            }

            if (differentMoveStyle) {
                var moveStyle = (MoveStyle) player.MoveStyle;
                var equipped = moveStyle != MoveStyle.ON_FOOT;
                reptilePlayer.SetCurrentMoveStyleEquipped(moveStyle);
                reptilePlayer.SwitchToEquippedMovestyle(equipped);
            }

            associatedPlayer.ResetPlayer(player);
        }
    }

    private void HandlePlayerPositionUpdate(ClientboundPlayerPositionUpdate playerPositionUpdate) {
        foreach (var kvp in playerPositionUpdate.Positions) {
            if (this.Players.TryGetValue(kvp.Key, out var associatedPlayer)) {
                associatedPlayer.SetPos(kvp.Value);
            }
        }
    }

    private void HandlePlayerScoreUpdate(ClientboundPlayerScoreUpdate playerScoreUpdate) {
        if (this.Players.TryGetValue(playerScoreUpdate.Player, out var associatedPlayer)) {
            associatedPlayer.Score = playerScoreUpdate.Score;
            associatedPlayer.Multiplier = playerScoreUpdate.Multiplier;
        }
    }

    private void HandlePlayerVisualUpdate(ClientboundPlayerVisualUpdate playerVisualUpdate) {
        if (this.Players.TryGetValue(playerVisualUpdate.Player, out var associatedPlayer)) {
            var reptilePlayer = associatedPlayer.ReptilePlayer;
            var traverse = Traverse.Create(reptilePlayer);
            var characterVisual = traverse.Field<CharacterVisual>("characterVisual").Value;

            var boostpackEffect = (BoostpackEffectMode) playerVisualUpdate.BoostpackEffect;
            var frictionEffect = (FrictionEffectMode) playerVisualUpdate.FrictionEffect;
            var spraycan = playerVisualUpdate.Spraycan;
            var phone = playerVisualUpdate.Phone;

            characterVisual.hasEffects = true;
            characterVisual.hasBoostPack = true;

            // TODO scale
            this.IsSettingVisual = true;
            characterVisual.SetBoostpackEffect(boostpackEffect);
            characterVisual.SetFrictionEffect(frictionEffect);
            characterVisual.SetSpraycan(spraycan);
            characterVisual.SetPhone(phone);
            associatedPlayer.PhoneOut = phone;
            this.IsSettingVisual = false;
        }
    }

    private void HandleRequestRace(ClientboundRequestRace clientboundRequestRace) {
        RaceManager.Instance.OnRaceRequestResponse(clientboundRequestRace.Response, clientboundRequestRace.RaceConfig);
    }

    private void HandleRaceInitialize() {
        RaceManager.Instance.OnRaceInitialize();
    }

    private void HandleRaceStart() {
        RaceManager.Instance.OnRaceStart();
    }

    private void HandleRaceRank(ClientboundRaceRank clientboundRaceRank) {
        RaceManager.Instance.OnRaceRank(clientboundRaceRank.Rank);
    }


    public void PlayAnimation(int anim, bool forceOverwrite, bool instant, float atTime) {
        // Sometimes the game likes to spam animations. Why? idk lol
        if (this.lastAnimation == anim) return;
        this.lastAnimation = anim;

        Plugin.NetworkConnection.SendMessage(new ServerboundAnimation {
            Animation = anim,
            ForceOverwrite = forceOverwrite,
            Instant = instant,
            AtTime = atTime
        });
    }
}
