using System;
using System.Collections.Generic;
using Godot;
using IdleLineage.Combat;
using IdleLineage.Network;

namespace IdleLineage.App;

public partial class ArpgEngineScreen
{
    private readonly Dictionary<string, (Combatant Actor, ArpgActor View)> _remotePlayerViews = new();
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

        foreach (var pair in _remotePlayerViews.Values)
        {
            _views.Remove(pair.Actor);
            try { pair.View.Free(); } catch { }
        }
        _remotePlayerViews.Clear();
    }

    private void MultiplayerStep(double delta)
    {
        NetworkManager.Instance.Update();

        if (!NetworkManager.Instance.IsConnected || _engine?.Player == null)
        {
            return;
        }

        // Sync each remote player view to Godot rendering tree
        float dt = (float)delta;
        foreach (var pair in _remotePlayerViews.Values)
        {
            pair.View.Pos = _engine.RenderPos(pair.Actor);
            pair.View.MinimumWorldDepth = ResolveOpaqueWorldObjectActorDepthFloor(pair.Actor.Pos);
            pair.View.Sync(0.0, dt);
        }

        _netSyncTimer += delta;
        if (_netSyncTimer >= 0.04) // 25Hz sync rate
        {
            _netSyncTimer = 0.0;
            var p = _engine.Player;
            Vector2 currentPos = PlayerPos();
            int currentFacing = p.Facing8;
            bool isMoving = _wasdMoving || p.MoveTarget.HasValue;

            if (currentPos.DistanceSquaredTo(_lastSentNetPos) > 1.0f || currentFacing != _lastSentNetFacing)
            {
                _lastSentNetPos = currentPos;
                _lastSentNetFacing = currentFacing;

                NetworkManager.Instance.SendMove(new MovePacket
                {
                    X = p.Pos.X,
                    Y = p.Pos.Y,
                    Facing8 = currentFacing,
                    Stepping = isMoving,
                    MapKey = _mapKey
                });
            }
        }
    }

    private void SendLocalHandshake()
    {
        if (_engine?.Player == null) return;
        var p = _engine.Player;
        NetworkManager.Instance.SendHandshake(new HandshakePacket
        {
            Name = p.Disp,
            ClassId = _build.ClassId,
            Avatar = _build.Avatar,
            WeaponPrefix = _build.WeaponPrefix,
            Level = p.Level,
            Hp = (int)p.Hp,
            MaxHp = (int)p.MaxHp,
            X = p.Pos.X,
            Y = p.Pos.Y,
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
            _views.Remove(existing.Actor);
            try { existing.View.Free(); } catch { }
            _remotePlayerViews.Remove(handshake.PlayerId);
        }

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
            Avatar = handshake.Avatar,
            Pos = new WorldPoint(handshake.X, handshake.Y),
            Facing8 = handshake.Facing8
        };

        // Create authentic view
        CharacterMorphAnimation.Spec spec = ResolveCharacterVisual(actor, handshake.Avatar, handshake.WeaponPrefix);
        ArpgActor view = ArpgActor.Create(_atlas, _arena, _ui, spec.Group, spec.Atlas, spec.WeaponPrefix, isPlayer: true, 1.0f, spec.ThreeDirection);
        view.VisualKey = CharacterVisualKey(actor);
        view.SetNameWithoutLevel(actor.Disp, actor.Level);
        view.SetNameColor(Color.FromHtml("#66d9ef")); // Teammate Cyan
        view.Pos = _engine.RenderPos(actor);
        view.FaceDirection(handshake.Facing8);
        view.Sync(0.0, 0f);

        _views[actor] = view;
        _remotePlayerViews[handshake.PlayerId] = (actor, view);

        SlabLog($"[color=#86efac]⚔️【隊友連線】{handshake.Name}（{handshake.ClassId} Lv.{handshake.Level}）已加入同地圖！[/color]");

        // If we are Host, respond with our handshake so client gets host immediately
        if (NetworkManager.Instance.IsHost)
        {
            SendLocalHandshake();
        }
    }

    private void HandleRemotePlayerMoved(MovePacket move)
    {
        if (_remotePlayerViews.TryGetValue(move.PlayerId, out var pair))
        {
            pair.Actor.Pos = new WorldPoint(move.X, move.Y);
            pair.Actor.Facing8 = move.Facing8;
            pair.View.Pos = _engine.RenderPos(pair.Actor);
            pair.View.FaceDirection(move.Facing8);
            pair.View.DriveLoop(move.Stepping);
        }
    }

    private void HandleRemotePlayerAction(ActionPacket action)
    {
        if (_remotePlayerViews.TryGetValue(action.PlayerId, out var pair))
        {
            if (action.ActionType == "attack")
            {
                pair.View.PlayAttack(_rng, rangedAttacker: false, rangedShot: false, cycleSeconds: 0.6, speedRatio: 1.0);
            }
            else if (action.ActionType == "cast")
            {
                PlayCastAnim(pair.View, pair.Actor, null, action.SkillId, true);
            }
        }
    }

    private void HandleRemotePlayerLeft(string playerId)
    {
        if (_remotePlayerViews.Remove(playerId, out var pair))
        {
            _views.Remove(pair.Actor);
            try { pair.View.Free(); } catch { }
            SlabLog($"[color=#fca5a5]🚪【隊友離線】{pair.Actor.Disp} 已退出遊戲。[/color]");
        }
    }

    private void HandleRemoteChatReceived(ChatPacket chat)
    {
        SlabLog($"[color={chat.ColorHex}]💬【{chat.SenderName}】{chat.Message}[/color]");
    }
}
