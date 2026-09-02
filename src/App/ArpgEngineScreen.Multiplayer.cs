using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IdleLineage.Combat;
using IdleLineage.Network;

namespace IdleLineage.App;

public partial class ArpgEngineScreen
{
    private readonly Dictionary<string, (Combatant Actor, ArpgActor View, bool IsMoving)> _remotePlayerViews = new();
    private double _netSyncTimer = 0.0;
    private double _netMobSyncTimer = 0.0;
    private Vector2 _lastSentNetPos;
    private int _lastSentNetFacing = -1;
    private string _lastSentWeaponId = "";
    private double _lastSentHp = -1.0;

    private void InitMultiplayer()
    {
        NetworkManager.Instance.OnRemotePlayerJoined += HandleRemotePlayerJoined;
        NetworkManager.Instance.OnRemotePlayerMoved += HandleRemotePlayerMoved;
        NetworkManager.Instance.OnRemotePlayerEquipped += HandleRemotePlayerEquipped;
        NetworkManager.Instance.OnPlayerHpSynced += HandlePlayerHpSynced;
        NetworkManager.Instance.OnRemotePlayerAction += HandleRemotePlayerAction;
        NetworkManager.Instance.OnChatReceived += HandleRemoteChatReceived;
        NetworkManager.Instance.OnRemotePlayerLeft += HandleRemotePlayerLeft;

        // Mob sync
        NetworkManager.Instance.OnMobSpawned += HandleMobSpawned;
        NetworkManager.Instance.OnMobBatchMoved += HandleMobBatchMoved;
        NetworkManager.Instance.OnMobHitReceived += HandleMobHitReceived;
        NetworkManager.Instance.OnMobHpSynced += HandleMobHpSynced;
        NetworkManager.Instance.OnMobDied += HandleMobDied;

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
        NetworkManager.Instance.OnRemotePlayerEquipped -= HandleRemotePlayerEquipped;
        NetworkManager.Instance.OnPlayerHpSynced -= HandlePlayerHpSynced;
        NetworkManager.Instance.OnRemotePlayerAction -= HandleRemotePlayerAction;
        NetworkManager.Instance.OnChatReceived -= HandleRemoteChatReceived;
        NetworkManager.Instance.OnRemotePlayerLeft -= HandleRemotePlayerLeft;

        NetworkManager.Instance.OnMobSpawned -= HandleMobSpawned;
        NetworkManager.Instance.OnMobBatchMoved -= HandleMobBatchMoved;
        NetworkManager.Instance.OnMobHitReceived -= HandleMobHitReceived;
        NetworkManager.Instance.OnMobHpSynced -= HandleMobHpSynced;
        NetworkManager.Instance.OnMobDied -= HandleMobDied;

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

            // Sync weapon change if local player switched weapon in bag
            string currentWeapon = _engine.Player.MainWeaponId ?? "";
            if (currentWeapon != _lastSentWeaponId)
            {
                _lastSentWeaponId = currentWeapon;
                NetworkManager.Instance.SendEquip(new EquipPacket
                {
                    MainWeaponId = currentWeapon,
                    WeaponPrefix = _build?.WeaponPrefix ?? ""
                });
            }

            _netSyncTimer += delta;
            if (_netSyncTimer >= 0.03) // ~30Hz sync rate
            {
                _netSyncTimer = 0.0;
                var p = _engine.Player;
                Vector2 currentPos = PlayerPos();
                int currentFacing = p.Facing8;
                bool isMoving = _wasdMoving || p.MoveTarget.HasValue;
                bool hpChanged = Math.Abs(p.Hp - _lastSentHp) > 0.01;

                if (currentPos.DistanceSquaredTo(_lastSentNetPos) > 0.01f || currentFacing != _lastSentNetFacing || hpChanged)
                {
                    _lastSentNetPos = currentPos;
                    _lastSentNetFacing = currentFacing;
                    _lastSentHp = p.Hp;

                    NetworkManager.Instance.SendMove(new MovePacket
                    {
                        X = currentPos.X,
                        Y = currentPos.Y,
                        Facing8 = currentFacing,
                        Stepping = isMoving,
                        Hp = p.Hp,
                        MaxHp = p.MaxHp,
                        MapKey = _mapKey
                    });
                }
            }

            // Host broadcasts living mob positions to clients at 10Hz
            if (NetworkManager.Instance.IsHost)
            {
                _netMobSyncTimer += delta;
                if (_netMobSyncTimer >= 0.10)
                {
                    _netMobSyncTimer = 0.0;
                    BroadcastHostMobs();
                }
            }
        }
        catch { }
    }

    private void BroadcastHostMobs()
    {
        try
        {
            var moves = new List<MobMoveEntry>();
            foreach (Combatant c in _engine.Combatants)
            {
                if (c.Kind == CombatantKind.Mob && !c.Dead)
                {
                    moves.Add(new MobMoveEntry
                    {
                        MobId = c.Key,
                        X = c.Pos.X,
                        Y = c.Pos.Y,
                        Facing8 = c.Facing8,
                        Stepping = c.MoveSpeed > 0.1
                    });
                }
            }
            if (moves.Count > 0)
            {
                NetworkManager.Instance.SendMobBatchMove(new MobBatchMovePacket { Moves = moves });
            }
        }
        catch { }
    }

    public void NoteHostSpawnedMob(Combatant mob, string mobKey)
    {
        if (!NetworkManager.Instance.IsConnected || !NetworkManager.Instance.IsHost) return;
        try
        {
            NetworkManager.Instance.SendMobSpawn(new MobSpawnPacket
            {
                MobId = mob.Key,
                MobKey = mobKey,
                X = mob.Pos.X,
                Y = mob.Pos.Y,
                Hp = mob.Hp,
                MaxHp = mob.MaxHp,
                Facing8 = mob.Facing8,
                MapKey = _mapKey
            });
        }
        catch { }
    }

    public void NoteCombatDamageEvent(Combatant source, Combatant target, double dmg)
    {
        if (!NetworkManager.Instance.IsConnected || target == null) return;
        try
        {
            if (target == _engine.Player)
            {
                // Local player took damage -> sync to other players
                NetworkManager.Instance.SendPlayerHpSync(new PlayerHpSyncPacket
                {
                    Hp = _engine.Player.Hp,
                    MaxHp = _engine.Player.MaxHp,
                    DamageTaken = dmg
                });
            }
            else if (target.Kind == CombatantKind.Mob)
            {
                if (NetworkManager.Instance.IsHost)
                {
                    // Host broadcasts HP sync to all clients
                    NetworkManager.Instance.SendMobHpSync(new MobHpSyncPacket
                    {
                        MobId = target.Key,
                        CurrentHp = target.Hp,
                        DamageTaken = dmg,
                        AttackerId = NetworkManager.Instance.LocalPlayerId
                    });
                }
                else if (source == _engine.Player)
                {
                    // Client tells Host it hit the mob
                    NetworkManager.Instance.SendMobHit(new MobHitPacket
                    {
                        MobId = target.Key,
                        Damage = dmg
                    });
                }
            }
        }
        catch { }
    }

    public void NoteCombatDeathEvent(Combatant target)
    {
        if (!NetworkManager.Instance.IsConnected || !NetworkManager.Instance.IsHost || target == null || target.Kind != CombatantKind.Mob) return;
        try
        {
            NetworkManager.Instance.SendMobDeath(new MobDeathPacket
            {
                MobId = target.Key,
                KillerId = NetworkManager.Instance.LocalPlayerId
            });
        }
        catch { }
    }

    private void SendLocalHandshake()
    {
        if (_engine?.Player == null) return;
        var p = _engine.Player;
        Vector2 pos = PlayerPos();
        string weaponId = string.IsNullOrEmpty(p.MainWeaponId) ? (_build?.WeaponPrefix ?? "sword1") : p.MainWeaponId;
        NetworkManager.Instance.SendHandshake(new HandshakePacket
        {
            Name = p.Disp,
            ClassId = _build?.ClassId ?? "knight",
            Avatar = _build?.Avatar ?? "男騎士",
            WeaponPrefix = _build?.WeaponPrefix ?? "sword1",
            MainWeaponId = weaponId,
            Level = p.Level,
            Hp = p.Hp,
            MaxHp = p.MaxHp,
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

        if (_remotePlayerViews.TryGetValue(handshake.PlayerId, out var existing))
        {
            try { existing.View.Free(); } catch { }
            _remotePlayerViews.Remove(handshake.PlayerId);
        }

        ClassDef? cdef = ClassCatalog.Find(handshake.ClassId);
        string avatar = string.IsNullOrEmpty(handshake.Avatar) ? (cdef?.MaleAvatar ?? "男騎士") : handshake.Avatar;
        string weapon = string.IsNullOrEmpty(handshake.WeaponPrefix) ? (cdef?.Weapon ?? "sword1") : handshake.WeaponPrefix;
        string mainWeapon = string.IsNullOrEmpty(handshake.MainWeaponId) ? weapon : handshake.MainWeaponId;

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
            MainWeaponId = mainWeapon,
            Pos = new WorldPoint(handshake.X, handshake.Y),
            Facing8 = handshake.Facing8
        };

        try
        {
            ArpgActor view = CreateView(actor);
            view.SetNameWithoutLevel(actor.Disp, actor.Level);
            view.SetNameColor(Color.FromHtml("#66d9ef")); // Teammate Cyan
            view.Hp = actor.Hp;
            view.MaxHp = actor.MaxHp;
            view.Pos = ToVec(actor.Pos);
            view.FaceDirection(handshake.Facing8);

            // Apply weapon visual immediately
            var (desired, fallback) = CharacterWeaponAnimation.Resolve(actor, GameDataProvider.Shared);
            view.SetWeaponPrefix(desired, fallback);
            view.Sync(1.0, 0f);

            _remotePlayerViews[handshake.PlayerId] = (actor, view, false);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Multiplayer] Create view warning: {ex.Message}");
        }

        SlabLog($"[color=#86efac]⚔️【隊友連線】{handshake.Name}（{handshake.ClassId} Lv.{handshake.Level}）已加入同地圖！[/color]");

        if (NetworkManager.Instance.IsHost)
        {
            SendLocalHandshake();
        }
    }

    private void HandleRemotePlayerEquipped(EquipPacket equip)
    {
        if (_remotePlayerViews.TryGetValue(equip.PlayerId, out var tuple))
        {
            tuple.Actor.MainWeaponId = equip.MainWeaponId;
            var (desired, fallback) = CharacterWeaponAnimation.Resolve(tuple.Actor, GameDataProvider.Shared);
            tuple.View.SetWeaponPrefix(desired, fallback);
            tuple.View.Sync(1.0, 0f);
        }
    }

    private void HandlePlayerHpSynced(PlayerHpSyncPacket hpSync)
    {
        if (_remotePlayerViews.TryGetValue(hpSync.PlayerId, out var tuple))
        {
            tuple.Actor.Hp = hpSync.Hp;
            tuple.Actor.MaxHp = hpSync.MaxHp;
            tuple.View.Hp = hpSync.Hp;
            tuple.View.MaxHp = hpSync.MaxHp;
            tuple.View.Sync(1.0, 0.016f);

            if (hpSync.DamageTaken > 0)
            {
                Float(ToVec(tuple.Actor.Pos), $"{(int)hpSync.DamageTaken}", Color.FromHtml("#ef4444"), big: false);
            }
        }
    }

    private void HandleRemotePlayerMoved(MovePacket move)
    {
        if (_remotePlayerViews.TryGetValue(move.PlayerId, out var tuple))
        {
            tuple.Actor.Pos = new WorldPoint(move.X, move.Y);
            tuple.Actor.Facing8 = move.Facing8;
            tuple.Actor.Hp = move.Hp;
            tuple.Actor.MaxHp = move.MaxHp;
            tuple.View.Hp = move.Hp;
            tuple.View.MaxHp = move.MaxHp;
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

    // --- Monster Synchronization Handlers ---

    private void HandleMobSpawned(MobSpawnPacket spawn)
    {
        if (NetworkManager.Instance.IsHost) return;
        if (spawn.MapKey != _mapKey) return;

        Combatant? existing = _engine.Combatants.FirstOrDefault(c => c.Key == spawn.MobId);
        if (existing == null)
        {
            try
            {
                Combatant mob = _engine.SpawnMob(spawn.MobKey, new WorldPoint(spawn.X, spawn.Y));
                mob.Key = spawn.MobId;
                mob.Hp = spawn.Hp;
                mob.MaxHp = spawn.MaxHp;
                mob.Facing8 = spawn.Facing8;
            }
            catch { }
        }
    }

    private void HandleMobBatchMoved(MobBatchMovePacket batch)
    {
        if (NetworkManager.Instance.IsHost) return;

        foreach (var entry in batch.Moves)
        {
            Combatant? mob = _engine.Combatants.FirstOrDefault(c => c.Key == entry.MobId);
            if (mob != null && !mob.Dead)
            {
                mob.Pos = new WorldPoint(entry.X, entry.Y);
                mob.Facing8 = entry.Facing8;
            }
        }
    }

    private void HandleMobHitReceived(MobHitPacket hit)
    {
        if (!NetworkManager.Instance.IsHost) return;

        Combatant? mob = _engine.Combatants.FirstOrDefault(c => c.Key == hit.MobId);
        if (mob != null && !mob.Dead)
        {
            mob.Hp = Math.Max(0.0, mob.Hp - hit.Damage);
            Float(ToVec(mob.Pos), $"{(int)hit.Damage}", Color.FromHtml("#ff5555"), big: false);

            NetworkManager.Instance.SendMobHpSync(new MobHpSyncPacket
            {
                MobId = mob.Key,
                CurrentHp = mob.Hp,
                DamageTaken = hit.Damage,
                AttackerId = hit.AttackerId
            });

            if (mob.Hp <= 0.0)
            {
                mob.Hp = 0;
                mob.Dead = true;
                NetworkManager.Instance.SendMobDeath(new MobDeathPacket
                {
                    MobId = mob.Key,
                    KillerId = hit.AttackerId
                });
            }
        }
    }

    private void HandleMobHpSynced(MobHpSyncPacket hpSync)
    {
        Combatant? mob = _engine.Combatants.FirstOrDefault(c => c.Key == hpSync.MobId);
        if (mob != null)
        {
            mob.Hp = hpSync.CurrentHp;
            Float(ToVec(mob.Pos), $"{(int)hpSync.DamageTaken}", Color.FromHtml("#ff5555"), big: false);
        }
    }

    private void HandleMobDied(MobDeathPacket death)
    {
        Combatant? mob = _engine.Combatants.FirstOrDefault(c => c.Key == death.MobId);
        if (mob != null && !mob.Dead)
        {
            mob.Hp = 0;
            mob.Dead = true;
        }
    }
}
