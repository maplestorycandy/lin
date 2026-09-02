using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using IdleLineage.Combat;
using IdleLineage.Network;

namespace IdleLineage.App;

public partial class ArpgEngineScreen
{
    private class RemotePlayerState
    {
        public Combatant Actor { get; set; } = null!;
        public ArpgActor View { get; set; } = null!;
        public bool IsMoving { get; set; }
        public float WalkProgress { get; set; }
        public double MoveHoldTimer { get; set; }
    }

    private readonly Dictionary<string, RemotePlayerState> _remotePlayerViews = new();
    private readonly Dictionary<string, (Vector2 TargetPos, int Facing8, bool Stepping)> _clientMobTargets = new();
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

        if (_engine?.Engine != null)
        {
            _engine.Engine.DisableMobAi = NetworkManager.Instance.IsConnected && !NetworkManager.Instance.IsHost;
        }

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

        if (_engine?.Engine != null)
        {
            _engine.Engine.DisableMobAi = false;
        }

        foreach (var state in _remotePlayerViews.Values)
        {
            try { _engine?.Engine?.Remove(state.Actor); } catch { }
            try { state.View.Free(); } catch { }
        }
        _remotePlayerViews.Clear();
        _clientMobTargets.Clear();
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

            if (_engine.Engine != null)
            {
                _engine.Engine.DisableMobAi = !NetworkManager.Instance.IsHost;
            }

            float dt = (float)delta;

            // 1. Update Remote Player Views & Walking Animation Progress
            foreach (var state in _remotePlayerViews.Values)
            {
                try
                {
                    if (state.MoveHoldTimer > 0)
                    {
                        state.MoveHoldTimer -= delta;
                        if (state.MoveHoldTimer <= 0)
                        {
                            state.IsMoving = false;
                        }
                    }

                    if (state.IsMoving)
                    {
                        state.WalkProgress = (state.WalkProgress + dt * 4.2f) % 1.0f;
                    }
                    else
                    {
                        state.WalkProgress = 0f;
                    }

                    UpdateView(state.View, state.Actor, (ToVec(state.Actor.Pos), state.WalkProgress, state.IsMoving), dt, false);
                }
                catch { }
            }

            // 2. Client-side Smooth Mob Movement Interpolation
            if (!NetworkManager.Instance.IsHost && _clientMobTargets.Count > 0)
            {
                foreach (var (mobId, info) in _clientMobTargets)
                {
                    Combatant? mob = _engine.Combatants.FirstOrDefault(c => c.Key == mobId);
                    if (mob != null && !mob.Dead)
                    {
                        Vector2 current = ToVec(mob.Pos);
                        float dist = current.DistanceTo(info.TargetPos);

                        if (dist > 180f)
                        {
                            mob.Pos = new WorldPoint(info.TargetPos.X, info.TargetPos.Y);
                        }
                        else if (dist > 0.5f)
                        {
                            float speed = Mathf.Max(120f, dist * 6.0f);
                            Vector2 next = current.MoveToward(info.TargetPos, speed * dt);
                            mob.Pos = new WorldPoint(next.X, next.Y);
                        }

                        mob.Facing8 = info.Facing8;
                        if (_views.TryGetValue(mob, out ArpgActor? view))
                        {
                            view.FaceDirection(info.Facing8);
                            view.DriveLoop(info.Stepping);
                        }
                    }
                }
            }

            // 3. Sync weapon change if local player switched weapon in bag
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

            // 4. Send Player Move & HP (30Hz)
            _netSyncTimer += delta;
            if (_netSyncTimer >= 0.03)
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

            // 5. Host broadcasts living mob positions to clients at 20Hz
            if (NetworkManager.Instance.IsHost)
            {
                _netMobSyncTimer += delta;
                if (_netMobSyncTimer >= 0.05)
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
                NetworkManager.Instance.SendPlayerHpSync(new PlayerHpSyncPacket
                {
                    Hp = _engine.Player.Hp,
                    MaxHp = _engine.Player.MaxHp,
                    DamageTaken = dmg
                });
            }
            else if (target.IsRemote)
            {
                // Host mob hit remote client player -> send damage to client
                if (NetworkManager.Instance.IsHost)
                {
                    NetworkManager.Instance.SendPlayerHpSync(new PlayerHpSyncPacket
                    {
                        PlayerId = target.Key,
                        Hp = target.Hp,
                        MaxHp = target.MaxHp,
                        DamageTaken = dmg
                    });
                }
            }
            else if (target.Kind == CombatantKind.Mob)
            {
                if (NetworkManager.Instance.IsHost)
                {
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
            try { _engine?.Engine?.Remove(existing.Actor); } catch { }
            try { existing.View.Free(); } catch { }
            _remotePlayerViews.Remove(handshake.PlayerId);
        }

        ClassDef? cdef = ClassCatalog.Find(handshake.ClassId);
        string avatar = string.IsNullOrEmpty(handshake.Avatar) ? (cdef?.MaleAvatar ?? "男騎士") : handshake.Avatar;
        string weapon = string.IsNullOrEmpty(handshake.WeaponPrefix) ? (cdef?.Weapon ?? "sword1") : handshake.WeaponPrefix;
        string mainWeapon = string.IsNullOrEmpty(handshake.MainWeaponId) ? weapon : handshake.MainWeaponId;

        var actor = new Combatant
        {
            Kind = CombatantKind.Player,
            IsRemote = true,
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
            // Register remote player into CombatEngine so Host monster AI sees and targets them!
            _engine?.Engine?.Add(actor);

            ArpgActor view = CreateView(actor);
            view.IsRemote = true;
            view.SetNameWithoutLevel(actor.Disp, actor.Level);
            view.SetNameColor(Color.FromHtml("#66d9ef"));
            view.Hp = actor.Hp;
            view.MaxHp = actor.MaxHp;
            view.Pos = ToVec(actor.Pos);
            view.FaceDirection(handshake.Facing8);

            var (desired, fallback) = CharacterWeaponAnimation.Resolve(actor, GameDataProvider.Shared);
            view.SetWeaponPrefix(desired, fallback);
            view.Sync(1.0, 0f);

            _remotePlayerViews[handshake.PlayerId] = new RemotePlayerState
            {
                Actor = actor,
                View = view,
                IsMoving = false,
                WalkProgress = 0f,
                MoveHoldTimer = 0.0
            };
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Multiplayer] Create view warning: {ex.Message}");
        }

        SlabLog($"[color=#86efac]⚔️【隊友連線】{handshake.Name}（{handshake.ClassId} Lv.{handshake.Level}）已加入同地圖！[/color]");

        if (NetworkManager.Instance.IsHost)
        {
            SendLocalHandshake();

            // Host syncs all current active monsters to the newly joined player!
            foreach (Combatant c in _engine.Combatants)
            {
                if (c.Kind == CombatantKind.Mob && !c.Dead)
                {
                    string mKey = string.IsNullOrEmpty(c.Avatar) ? "goblin" : c.Avatar;
                    NetworkManager.Instance.SendMobSpawn(new MobSpawnPacket
                    {
                        MobId = c.Key,
                        MobKey = mKey,
                        X = c.Pos.X,
                        Y = c.Pos.Y,
                        Hp = c.Hp,
                        MaxHp = c.MaxHp,
                        Facing8 = c.Facing8,
                        MapKey = _mapKey
                    });
                }
            }
        }
    }

    private void HandleRemotePlayerEquipped(EquipPacket equip)
    {
        if (_remotePlayerViews.TryGetValue(equip.PlayerId, out var state))
        {
            state.Actor.MainWeaponId = equip.MainWeaponId;
            var (desired, fallback) = CharacterWeaponAnimation.Resolve(state.Actor, GameDataProvider.Shared);
            state.View.SetWeaponPrefix(desired, fallback);
            state.View.Sync(1.0, 0f);
        }
    }

    private void HandlePlayerHpSynced(PlayerHpSyncPacket hpSync)
    {
        if (hpSync.PlayerId == NetworkManager.Instance.LocalPlayerId || string.IsNullOrEmpty(hpSync.PlayerId))
        {
            // Local player was damaged
            if (hpSync.DamageTaken > 0 && _engine?.Player != null)
            {
                Float(PlayerPos(), $"{(int)hpSync.DamageTaken}", Color.FromHtml("#ef4444"), big: false);
            }
            return;
        }

        if (_remotePlayerViews.TryGetValue(hpSync.PlayerId, out var state))
        {
            state.Actor.Hp = hpSync.Hp;
            state.Actor.MaxHp = hpSync.MaxHp;
            state.Actor.Dead = hpSync.Hp <= 0;
            state.View.Hp = hpSync.Hp;
            state.View.MaxHp = hpSync.MaxHp;
            state.View.Sync(1.0, 0.016f);

            if (hpSync.DamageTaken > 0)
            {
                Float(ToVec(state.Actor.Pos), $"{(int)hpSync.DamageTaken}", Color.FromHtml("#ef4444"), big: false);
            }
        }
    }

    private void HandleRemotePlayerMoved(MovePacket move)
    {
        if (_remotePlayerViews.TryGetValue(move.PlayerId, out var state))
        {
            state.Actor.Pos = new WorldPoint(move.X, move.Y);
            state.Actor.Facing8 = move.Facing8;
            state.Actor.Hp = move.Hp;
            state.Actor.MaxHp = move.MaxHp;
            state.Actor.Dead = move.Hp <= 0;
            state.View.Hp = move.Hp;
            state.View.MaxHp = move.MaxHp;
            state.View.FaceDirection(move.Facing8);
            state.IsMoving = move.Stepping;
            state.MoveHoldTimer = 0.15;
        }
    }

    private void HandleRemotePlayerAction(ActionPacket action)
    {
        if (_remotePlayerViews.TryGetValue(action.PlayerId, out var state))
        {
            if (action.ActionType == "attack")
            {
                state.View.PlayAttack(_rng, rangedAttacker: false, rangedShot: false, cycleSeconds: 0.6, speedRatio: 1.0);
            }
            else if (action.ActionType == "cast")
            {
                PlayCastAnim(state.View, state.Actor, null, action.SkillId, true);
            }
        }
    }

    private void HandleRemotePlayerLeft(string playerId)
    {
        if (_remotePlayerViews.Remove(playerId, out var state))
        {
            try { _engine?.Engine?.Remove(state.Actor); } catch { }
            try { state.View.Free(); } catch { }
            SlabLog($"[color=#fca5a5]🚪【隊友離線】{state.Actor.Disp} 已退出遊戲。[/color]");
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
                _clientMobTargets[spawn.MobId] = (new Vector2((float)spawn.X, (float)spawn.Y), spawn.Facing8, false);
            }
            catch { }
        }
        else
        {
            existing.Hp = spawn.Hp;
            existing.MaxHp = spawn.MaxHp;
            existing.Pos = new WorldPoint(spawn.X, spawn.Y);
            existing.Facing8 = spawn.Facing8;
            _clientMobTargets[spawn.MobId] = (new Vector2((float)spawn.X, (float)spawn.Y), spawn.Facing8, false);
        }
    }

    private void HandleMobBatchMoved(MobBatchMovePacket batch)
    {
        if (NetworkManager.Instance.IsHost) return;

        foreach (var entry in batch.Moves)
        {
            _clientMobTargets[entry.MobId] = (new Vector2((float)entry.X, (float)entry.Y), entry.Facing8, entry.Stepping);
        }
    }

    private void HandleMobHitReceived(MobHitPacket hit)
    {
        if (!NetworkManager.Instance.IsHost) return;

        Combatant? mob = _engine.Combatants.FirstOrDefault(c => c.Key == hit.MobId);
        Combatant? attacker = _engine.Combatants.FirstOrDefault(c => c.Key == hit.AttackerId);

        if (mob != null && !mob.Dead)
        {
            mob.Hp = Math.Max(0.0, mob.Hp - hit.Damage);
            if (attacker != null)
            {
                // Mob generates hate towards client and retaliates!
                _engine.Engine.AddHateExternal(mob, attacker, hit.Damage);
            }

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
                _clientMobTargets.Remove(mob.Key);
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
        _clientMobTargets.Remove(death.MobId);
        Combatant? mob = _engine.Combatants.FirstOrDefault(c => c.Key == death.MobId);
        if (mob != null && !mob.Dead)
        {
            mob.Hp = 0;
            mob.Dead = true;
        }
    }
}
