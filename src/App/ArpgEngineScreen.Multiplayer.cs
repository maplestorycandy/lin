using System;
using System.Collections.Generic;
using Godot;
using IdleLineage.Combat;
using IdleLineage.Network;

namespace IdleLineage.App;

public partial class ArpgEngineScreen
{
    private readonly Dictionary<string, (Combatant Actor, ArpgActor View, bool IsMoving)> _remotePlayerViews = new();
    private double _netSyncTimer = 0.0;
    private Vector2 _lastSentNetPos;
    private int _lastSentNetFacing = -1;

    private void InitMultiplayer()
    {
        NetworkManager.Instance.OnRemotePlayerJoined += HandleRemotePlayerJoined;
        NetworkManager.Instance.OnRemotePlayerMoved += HandleRemotePlayerMoved;
        NetworkManager.Instance.OnRemotePlayerAction += HandleRemotePlayerAction;
        NetworkManager.Instance.OnChatReceived += HandleRemoteChatReceived;
        NetworkManager.Instance.OnRemotePlayerLeft += HandleRemotePlayerLeft;

        if (NetworkManager.Instance.IsConnected)
        {
            SendLocalHandshake();
            SlabLog("[color=#86efac]🌐【多人連線已啟用】已同步至多人房間！[/color]");
        }
    }

    private void CleanupMultiplayer()
    {
        NetworkManager.Instance.OnRemotePlayerJoined -= HandleRemotePlayerJoined;
        NetworkManager.Instance.OnRemotePlayerMoved -= HandleRemotePlayerMoved;
        NetworkManager.Instance.OnRemotePlayerAction -= HandleRemotePlayerAction;
        NetworkManager.Instance.OnChatReceived -= HandleRemoteChatReceived;
        NetworkManager.Instance.OnRemotePlayerLeft -= HandleRemotePlayerLeft;

        foreach (var tuple in _remotePlayerViews.Values)
        {
            try { tuple.View.Free(); } catch { }
        }
        _remotePlayerViews.Clear();
    }

    private void MultiplayerStep(double delta)
    {
        try
        {
            NetworkManager.Instance.Update();

            if (!NetworkManager.Instance.IsConnected || _engine?.Player == null)
            {
                return;
            }

            float dt = (float)delta;
            foreach (var tuple in _remotePlayerViews.Values)
            {
                try
                {
                    UpdateView(tuple.View, tuple.Actor, (ToVec(tuple.Actor.Pos), 0f, tuple.IsMoving), dt, false);
                }
                catch { }
            }

            _netSyncTimer += delta;
            if (_netSyncTimer >= 0.03) // ~30Hz sync rate
            {
                _netSyncTimer = 0.0;
                var p = _engine.Player;
                Vector2 currentPos = PlayerPos();
                int currentFacing = p.Facing8;
                bool isMoving = _wasdMoving || p.MoveTarget.HasValue;

                if (currentPos.DistanceSquaredTo(_lastSentNetPos) > 0.01f || currentFacing != _lastSentNetFacing)
                {
                    _lastSentNetPos = currentPos;
                    _lastSentNetFacing = currentFacing;

                    NetworkManager.Instance.SendMove(new MovePacket
                    {
                        X = currentPos.X,
                        Y = currentPos.Y,
                        Facing8 = currentFacing,
                        Stepping = isMoving,
                        MapKey = _mapKey
                    });
                }
            }
        }
        catch { }
    }

    private void SendLocalHandshake()
    {
        if (_engine?.Player == null) return;
        var p = _engine.Player;
        Vector2 pos = PlayerPos();
        NetworkManager.Instance.SendHandshake(new HandshakePacket
        {
            Name = p.Disp,
            ClassId = _build?.ClassId ?? "knight",
            Avatar = _build?.Avatar ?? "男騎士",
            WeaponPrefix = _build?.WeaponPrefix ?? "sword1",
            Level = p.Level,
            Hp = (int)p.Hp,
            MaxHp = (int)p.MaxHp,
            X = pos.X,
            Y = pos.Y,
            Facing8 = p.Facing8,
            MapKey = _mapKey
        });
    }

    private void SendLocalAction(string actionType, string skillId = "", double targetX = 0, double targetY = 0)
    {
        if (!NetworkManager.Instance.IsConnected) return;
        NetworkManager.Instance.SendAction(new ActionPacket
        {
            ActionType = actionType,
            SkillId = skillId,
            TargetX = targetX,
            TargetY = targetY
        });
    }

    private void HandleRemotePlayerJoined(HandshakePacket handshake)
    {
        if (string.IsNullOrEmpty(handshake.PlayerId)) return;

        // Clean up previous view if rejoining
        if (_remotePlayerViews.TryGetValue(handshake.PlayerId, out var existing))
        {
            try { existing.View.Free(); } catch { }
            _remotePlayerViews.Remove(handshake.PlayerId);
        }

        ClassDef? cdef = ClassCatalog.Find(handshake.ClassId);
        string avatar = string.IsNullOrEmpty(handshake.Avatar) ? (cdef?.MaleAvatar ?? "男騎士") : handshake.Avatar;
        string weapon = string.IsNullOrEmpty(handshake.WeaponPrefix) ? (cdef?.Weapon ?? "sword1") : handshake.WeaponPrefix;

        // Create Combatant representation
        var actor = new Combatant
        {
            Kind = CombatantKind.Ally,
            Key = handshake.PlayerId,
            Disp = handshake.Name,
            Level = handshake.Level,
            MaxHp = handshake.MaxHp,
            Hp = handshake.Hp,
            MaxMp = 100,
            Mp = 100,
            ClassId = handshake.ClassId,
            Avatar = avatar,
            Pos = new WorldPoint(handshake.X, handshake.Y),
            Facing8 = handshake.Facing8
        };

        try
        {
            // Create authentic view using game's built-in CreateView!
            ArpgActor view = CreateView(actor);
            view.SetNameWithoutLevel(actor.Disp, actor.Level);
            view.SetNameColor(Color.FromHtml("#66d9ef")); // Teammate Cyan
            view.Pos = ToVec(actor.Pos);
            view.FaceDirection(handshake.Facing8);
            view.Sync(1.0, 0f);

            _remotePlayerViews[handshake.PlayerId] = (actor, view, false);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Multiplayer] Create view warning: {ex.Message}");
        }

        SlabLog($"[color=#86efac]⚔️【隊友連線】{handshake.Name}（{handshake.ClassId} Lv.{handshake.Level}）已加入同地圖！[/color]");

        // If we are Host, respond with our handshake so client gets host immediately
        if (NetworkManager.Instance.IsHost)
        {
            SendLocalHandshake();
        }
    }

    private void HandleRemotePlayerMoved(MovePacket move)
    {
        if (_remotePlayerViews.TryGetValue(move.PlayerId, out var tuple))
        {
            tuple.Actor.Pos = new WorldPoint(move.X, move.Y);
            tuple.Actor.Facing8 = move.Facing8;
            tuple.View.Pos = ToVec(tuple.Actor.Pos);
            tuple.View.FaceDirection(move.Facing8);
            tuple.View.DriveLoop(move.Stepping);
            _remotePlayerViews[move.PlayerId] = (tuple.Actor, tuple.View, move.Stepping);
        }
    }

    private void HandleRemotePlayerAction(ActionPacket action)
    {
        if (_remotePlayerViews.TryGetValue(action.PlayerId, out var tuple))
        {
            if (action.ActionType == "attack")
            {
                tuple.View.PlayAttack(_rng, rangedAttacker: false, rangedShot: false, cycleSeconds: 0.6, speedRatio: 1.0);
            }
            else if (action.ActionType == "cast")
            {
                PlayCastAnim(tuple.View, tuple.Actor, null, action.SkillId, true);
            }
        }
    }

    private void HandleRemotePlayerLeft(string playerId)
    {
        if (_remotePlayerViews.Remove(playerId, out var tuple))
        {
            try { tuple.View.Free(); } catch { }
            SlabLog($"[color=#fca5a5]🚪【隊友離線】{tuple.Actor.Disp} 已退出遊戲。[/color]");
        }
    }

    private void HandleRemoteChatReceived(ChatPacket chat)
    {
        SlabLog($"[color={chat.ColorHex}]💬【{chat.SenderName}】{chat.Message}[/color]");
    }
}
