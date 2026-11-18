using ModularEncountersSystems.API;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Cython.GuidedMissiles
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class GuidedMissileSystem : MySessionComponentBase
    {
        public const ushort NetworkId = (ushort)(3496380795 % ushort.MaxValue);
        // Ammo with the SLBM (aquatic) behaviour: leaves the launcher underwater, creeps straight until it
        // breaks the surface, then accelerates and guides. To give another missile this behaviour, add its
        // ammo subtype here and to IsMissile; the Iron Dome whitelist (MissileTurret) is separate.
        public static readonly HashSet<string> AquaticAmmoSubtypes = new HashSet<string>
        {
            "AMissile",          // missilSLBM-misterplace
            "LauncherXMissile",  // LauncherX
        };

        public static bool IsAquaticAmmo(string subtype)
        {
            return subtype != null && AquaticAmmoSubtypes.Contains(subtype);
        }

        // Aquatic ammo that stays at its MissileInitialSpeed while above water instead of accelerating
        // (the original SLBM behaviour). Ammo not listed here accelerates with its own stats once it surfaces.
        public static readonly HashSet<string> HoldInitialSpeedAboveWaterSubtypes = new HashSet<string>
        {
            "AMissile",
        };

        public static bool HoldsInitialSpeedAboveWater(string subtype)
        {
            return subtype != null && HoldInitialSpeedAboveWaterSubtypes.Contains(subtype);
        }

        public Network Net;
        Packet TrackerPacket;
        private MESApi _MESApi;
        private bool _npcTrackerHooked;
        private const double RAYCAST_DISTANCE = 2000.0;
        private int delay;
        private uint delay100;
        private bool targetLocked;
        private bool sentToServer;

        public class MissileGuider
        {
            public IMyMissile missile;
            MyMissileAmmoDefinition ammo;
            public bool isAquaticMissile;
            public bool isUnderwater;
            private bool hasBeenUnderwater;
            public IMyEntity target;
            public IMyLargeTurretBase missileLauncherTurret;
            public bool targetIsMissile;

            private Vector3D lastLeadPosition = Vector3D.Zero;

            private double time = 0;

            private const float ticktime = 1f / 60f;

            Vector3D direction = Vector3D.Zero;
            Vector3D velocity = Vector3D.Zero;
            private float speed;
            private double desiredSpeed = 0;
            public MyParticleEffect effect;

            Vector3D lastTargetPosition = Vector3D.Zero;
            private double lastDistanceToTarget = 0;
            private bool targetMissed = false;

            Vector3D position = Vector3D.Zero;

            public MissileGuider(IMyMissile missile, IMyEntity target, IMyLargeTurretBase missileLauncherTurret)
            {
                this.missile = missile;
                ammo = (MyMissileAmmoDefinition)missile.AmmoDefinition;
                desiredSpeed = ammo.DesiredSpeed * ammo.DesiredSpeed * 0.99f;

                if (IsAquaticAmmo(missile.AmmoDefinition.Id.SubtypeName))
                {
                    isAquaticMissile = true;
                    isUnderwater = true;
                }

                this.target = target;
                this.missileLauncherTurret = missileLauncherTurret;

                IMyMissile missileTarget = target as IMyMissile;

                if (missileTarget != null)
                {
                    targetIsMissile = true;
                }

                direction = this.missile.WorldMatrix.Forward;

                position = this.missile.GetPosition();

                if (target != null)
                {
                    lastTargetPosition = target.GetPosition();

                    lastDistanceToTarget = Vector3D.DistanceSquared(position, lastTargetPosition);
                }

                UpdateVelocity();
            }

            public Vector3D GetTargetPrediction()
            {
                if (target.MarkedForClose)
                {

                    IMyEntity newTarget = GetTargetEntity(missileLauncherTurret);

                    if (newTarget != null && !targetIsMissile)
                    {
                        IMyEntity newTargetBlock = GetTarget(newTarget, missile);

                        if ((newTargetBlock != null) && (newTargetBlock != target))
                        {
                            target = newTargetBlock;
                        }
                    }

                    return lastLeadPosition;
                }

                if (targetMissed)
                {
                    return lastLeadPosition;
                }

                Vector3D missilePosition = missile.GetPosition();
                Vector3D missileVelocity = velocity;

                if (flares.Any())
                {
                    foreach (var pair in flares)
                    {
                        Vector3D flarePosition = pair.Value.flare.GetPosition();

                        if (MissileTurret.MissileIsEnemy(missile, pair.Value.flare.Owner) && Vector3D.DistanceSquared(flarePosition, missilePosition) <= 10000)
                        {
                            target = pair.Value.flare;
                            break;
                        }
                    }
                }

                MyPhysicsComponentBase targetPhysics = null;

                if (target.Physics != null)
                    targetPhysics = target.Physics;

                if (targetPhysics == null && target.Parent != null && target.Parent.Physics != null)
                    targetPhysics = target.Parent.Physics;

                Vector3D targetPosition = target.GetPosition();

                double distanceToTarget = Vector3D.DistanceSquared(missilePosition, targetPosition);

                if (distanceToTarget > lastDistanceToTarget)
                {
                    if (missileVelocity.LengthSquared() >= desiredSpeed)
                    {
                        targetMissed = true;

                        if (missile.AmmoDefinition.Id.SubtypeName == "IDMissile")
                        {
                            lock (MissileTurret.missilesOnTarget)
                            {
                                foreach (var missileOnTarget in MissileTurret.missilesOnTarget)
                                {
                                    if (missileOnTarget.missile.EntityId == target.EntityId)
                                    {
                                        missileOnTarget.inIntercept = false;
                                        missileOnTarget.launcherId = 0;
                                        missileOnTarget.time = 0;
                                        break;
                                    }
                                }
                            }
                        }

                        return lastLeadPosition;
                    }
                }

                Vector3D targetVelocity;
                if (targetPhysics != null)
                {
                    targetVelocity = targetPhysics.LinearVelocity;
                }
                else
                {
                    targetVelocity = (targetPosition - lastTargetPosition) * 60;
                }

                Vector3D deltaPosition = targetPosition - missilePosition;
                Vector3D deltaVelocity = targetVelocity - missileVelocity;

                Vector3D leadPosition = targetPosition + (deltaPosition.Length() / deltaVelocity.Length()) * targetVelocity;

                lastLeadPosition = leadPosition;
                lastTargetPosition = targetPosition;
                lastDistanceToTarget = distanceToTarget;

                return leadPosition;
            }

            public static IMyEntity GetTargetEntity(IMyLargeTurretBase missileLauncherTurret)
            {
                if (missileLauncherTurret != null)
                {
                    Sandbox.ModAPI.Ingame.MyDetectedEntityInfo info = missileLauncherTurret.GetTargetedEntity();

                    if (!info.IsEmpty() && info.EntityId != 0)
                    {
                        return MyAPIGateway.Entities.GetEntityById(info.EntityId);
                    }
                }

                return null;
            }

            public static IMyEntity GetTarget(IMyEntity newTarget, IMyMissile interceptMissile)
            {
                if (interceptMissile == null || interceptMissile.MarkedForClose)
                {
                    return null;
                }

                IMyCubeGrid targetGrid = newTarget as IMyCubeGrid;

                if (targetGrid != null && interceptMissile.AmmoDefinition.Id.SubtypeName != "IDMissile")
                {
                    newTarget = targetGrid.GetFatBlocks<IMyThrust>().Count() > 0 ? targetGrid.GetFatBlocks<IMyThrust>().First() : null;

                    if (newTarget == null)
                    {
                        return targetGrid.GetFatBlocks<IMyTerminalBlock>().Count() > 0 ? targetGrid.GetFatBlocks<IMyTerminalBlock>().First() : null;
                    }

                    return newTarget;
                }

                IMyMissile targetMissile = newTarget as IMyMissile;
                IMyCharacter targetPlayer = newTarget as IMyCharacter;

                if (targetMissile != null && interceptMissile.AmmoDefinition.Id.SubtypeName == "IDMissile")
                {
                    for (int i = 0; i < MissileTurret.missilesOnTarget.Count(); i++)
                    {
                        if (MissileTurret.missilesOnTarget[i].missile == targetMissile)
                        {
                            MissileTurret.missilesOnTarget[i].inIntercept = true;
                            break;
                        }
                    }

                    return newTarget;
                }

                if (targetPlayer != null && interceptMissile.AmmoDefinition.Id.SubtypeName != "IDMissile")
                {
                    return newTarget;
                }

                return null;
            }

            public void Update()
            {
                if (isAquaticMissile)
                {
                    if (Jakaria.API.WaterModAPI.IsUnderwater(missile.GetPosition()))
                    {
                        isUnderwater = true;
                        hasBeenUnderwater = true;
                    }

                    else
                    {
                        // Only a missile that was really in the water can "surface". One launched in the air
                        // (or in a world with no water) must not splash: the Water Mod throws there.
                        bool surfacing = isUnderwater && hasBeenUnderwater;
                        isUnderwater = false;

                        if (surfacing && effect == null)
                        {
                            Vector3D worldPos = missile.WorldMatrix.Translation;
                            MyParticlesManager.TryCreateParticleEffect("Smoke_Missile", ref MatrixD.Identity, ref worldPos, missile.Render.GetRenderObjectID(), out effect);

                            try
                            {
                                Jakaria.API.WaterModAPI.CreateSplash(worldPos, 20f, true);
                                Jakaria.API.WaterModAPI.CreatePhysicsSplash(worldPos, velocity, 5f, 5);
                            }
                            catch (Exception e)
                            {
                                // A failing splash must never take the session down.
                                VRage.Utils.MyLog.Default.WriteLineAndConsole("[GuidedMissiles] Water splash failed: " + e.Message);
                            }
                        }

                        // Zeroing time to make aquatic missiles don't go to full speed when out of water.
                        // Held every tick above water for ammo in HoldInitialSpeedAboveWater (SLBM, as before);
                        // other aquatic ammo only restarts its own acceleration once, when it surfaces.
                        if (surfacing || HoldsInitialSpeedAboveWater(missile.AmmoDefinition.Id.SubtypeName))
                            time = 0;
                    }
                }

                if (target != null)
                {
                    UpdateDirection();
                }

                UpdateVelocity();
                UpdateTransform();

                if (time > 60 && speed < 0.5f)
                {
                    missile.Close();
                }

                time += ticktime;
            }

            public void UpdateVelocity()
            {
                //MyMissileAmmoDefinition ammo = (MyMissileAmmoDefinition)missile.AmmoDefinition;
                //double speed = Math.Min(ammo.MissileInitialSpeed + ammo.MissileAcceleration * time, ammo.DesiredSpeed);
                
                if (isAquaticMissile && isUnderwater)
                {
                    if (isUnderwater)
                    {
                        speed = (float)Math.Max(10f - (0.50f * time), 0f);
                        velocity = direction * speed;
                    }

                    else
                    {
                        velocity = direction * Math.Min(speed + ammo.MissileAcceleration * time, ammo.DesiredSpeed);
                    }
                }

                else
                {
                    velocity = direction * Math.Min(ammo.MissileInitialSpeed + ammo.MissileAcceleration * time, ammo.DesiredSpeed); ;
                }

                if (missile.Physics != null)
                    missile.Physics.LinearVelocity = velocity;
            }

            public void UpdateDirection()
            {
                if (isAquaticMissile && isUnderwater)
                {
                    /*
                    if (speed < 2f)
                    {

                    }
                    */

                    return;
                }

                Vector3D predictionPosition = GetTargetPrediction();

                Vector3D predictionDirection = predictionPosition - missile.GetPosition();
                predictionDirection.Normalize();

                double angle = Math.Acos(Math.Max(Math.Min(Vector3D.Dot(direction, predictionDirection), 1), -1));
                double turnLimit = 0.02d;

                if (missile.AmmoDefinition.Id.SubtypeName == "IDMissile")
                {
                    turnLimit = 0.08d;
                }

                Vector3D rotationAxis = Vector3D.Cross(direction, predictionDirection);
                rotationAxis.Normalize();

                if (!target.MarkedForClose && !targetMissed)
                {
                    MatrixD rotationMatrix = MatrixD.CreateFromQuaternion(
                        QuaternionD.CreateFromAxisAngle(rotationAxis, (double)(Math.Max(Math.Min(angle, turnLimit), -turnLimit)))
                    );
                    direction = Vector3D.Transform(direction, rotationMatrix);
                    direction.Normalize();
                }
            }

            public void UpdateTransform()
            {
                if (missile.Physics == null)
                {
                    position = position + velocity * ticktime;
                    missile.SetPosition(position);
                }

                MatrixD worldMatrix = missile.WorldMatrix;
                worldMatrix.Forward = direction;
                missile.WorldMatrix = worldMatrix;
            }
        }

        public class Flare
        {
            public IMyMissile flare { get; set; }
            public uint life { get; set; }

            public Flare(IMyMissile flare)
            {
                this.flare = flare;
                life = 0;
            }
        }

        public class RocketLauncherTracker
        {
            public long targetGrid;

            public RocketLauncherTracker(long targetGrid)
            {
                this.targetGrid = targetGrid;
            }
        }

        public static Dictionary<long, MissileGuider> missileGuiders = new Dictionary<long, MissileGuider>();
        private Dictionary<long, RocketLauncherTracker> rocketLauncherTrackers = new Dictionary<long, RocketLauncherTracker>();
        private static Dictionary<long, Flare> flares = new Dictionary<long, Flare>();
        private Dictionary<long, IMyEntity> _npcTargets = new Dictionary<long, IMyEntity>();

        public override void LoadData()
        {
            Net = new Network(NetworkId, ModContext.ModName);
            Net.SerializeTest = true;
            TrackerPacket = new Packet();
            Packet.OnReceive += Packet_OnReceive;
            _MESApi = new MESApi();
        }

        public override void BeforeStart()
        {
            MyAPIGateway.Missiles.OnMissileAdded += OnMissileAdded;
            MyAPIGateway.Missiles.OnMissileRemoved += OnMissileRemoved;
        }

        protected override void UnloadData()
        {
            Net?.Dispose();
            Net = null;
            Packet.OnReceive -= Packet_OnReceive;
            MyAPIGateway.Missiles.OnMissileAdded -= OnMissileAdded;
            MyAPIGateway.Missiles.OnMissileRemoved -= OnMissileRemoved;
            if (_MESApi.MESApiReady)
            {
                _MESApi.BehaviorTriggerActivationWatcher(false, GetNPCTarget);
                _MESApi.UnregisterListener();
            }
        }

        void Packet_OnReceive(Packet packet, ref PacketInfo packetInfo, ulong senderSteamId)
        {
            long playerIdentityId = MyAPIGateway.Players.TryGetIdentityId(packet.OriginalSenderSteamId);

            if (!rocketLauncherTrackers.ContainsKey(playerIdentityId))
            {
                rocketLauncherTrackers.Add(playerIdentityId, new RocketLauncherTracker(packet.targetGrid));
            }

            if (packet.clear)
            {
                if (rocketLauncherTrackers.ContainsKey(playerIdentityId))
                {
                    rocketLauncherTrackers.Remove(playerIdentityId);
                }
            }

            packetInfo.Relay = RelayMode.ToEveryone;

        }

        public void GetNPCTarget(IMyRemoteControl npcRemote, string triggerProfile, string actionProfile, IMyEntity target, Vector3D behaviorWaypoint)
        {
            if (npcRemote == null)
            {
                return;
            }

            if (npcRemote.CubeGrid == null)
            {
                return;
            }

            _npcTargets[npcRemote.CubeGrid.EntityId] = target;
        }

        private void OnMissileAdded(IMyMissile missile)
        {
            if (missile.AmmoDefinition.Id.SubtypeName == "Flare")
            {
                flares[missile.EntityId] = new Flare(missile);
                return;
            }

            if (!IsMissile(missile))
                return;

            IMyEntity target = null;
            IMyLargeTurretBase launcher = null;

            List<IMyPlayer> myPlayers = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(myPlayers, (player) => player.IdentityId == missile.Owner || player.Controller?.ControlledEntity?.Entity?.EntityId == missile.LauncherId);

            if (myPlayers.Count > 0 && target == null)
            {
                IMyPlayer player = myPlayers.First();
                target = player?.Character?.Components?.Get<MyTargetLockingComponent>()?.TargetEntity;

                IMyCubeGrid targetFromCockpit = target as IMyCubeGrid;

                if (targetFromCockpit != null && player != null)
                {
                    if (CheckOwnership(targetFromCockpit, player.IdentityId))
                    {
                        target = null;
                    }
                }
            }

            if (target == null)
            {
                IMyEntity entity = MyAPIGateway.Entities.GetEntityById(missile.LauncherId);

                if (entity != null)
                {
                    launcher = entity as IMyLargeTurretBase;
                    target = MissileGuider.GetTargetEntity(launcher);

                    if (target == null && _npcTargets.Count > 0)
                    {
                        IMySmallMissileLauncher smallMissileLauncher = entity as IMySmallMissileLauncher;
                        if (smallMissileLauncher != null)
                        {
                            _npcTargets.TryGetValue(smallMissileLauncher.CubeGrid.EntityId, out target);
                            goto IronDomeTest;
                        }

                        IMySmallMissileLauncherReload smallMissileLauncherReload = entity as IMySmallMissileLauncherReload;
                        if (smallMissileLauncherReload != null)
                        {
                            _npcTargets.TryGetValue(smallMissileLauncherReload.CubeGrid.EntityId, out target);
                        }
                    }

                    IronDomeTest:

                    if (target == null && missile.AmmoDefinition.Id.SubtypeName == "IDMissile")
                    {
                        if (MissileLauncher.missileLauncherTarget.ContainsKey(missile.LauncherId))
                        {
                            target = MyAPIGateway.Entities.GetEntityById(MissileLauncher.missileLauncherTarget[missile.LauncherId].EntityId);
                        }

                        else if (MissileTurret.missileTurretTarget.ContainsKey(missile.LauncherId))
                        {
                            target = MyAPIGateway.Entities.GetEntityById(MissileTurret.missileTurretTarget[missile.LauncherId].EntityId);
                        }
                    }
                }
            }

            if ((target == null) && rocketLauncherTrackers.ContainsKey(missile.Owner))
            {
                target = MyAPIGateway.Entities.GetEntityById(rocketLauncherTrackers[missile.Owner].targetGrid);
            }

            target = MissileGuider.GetTarget(target, missile);

            if (target != null || IsAquaticAmmo(missile.AmmoDefinition.Id.SubtypeName))
            {
                missile.Synchronized = true;
                missileGuiders.Add(missile.EntityId, new MissileGuider(missile, target, launcher));
            }
        }

        private bool IsMissile(IMyMissile missile)
        {
            string subtype = missile.AmmoDefinition.Id.SubtypeName;
            return subtype == "Missile" || subtype == "PMissile" || subtype == "IDMissile" || IsAquaticAmmo(subtype) || subtype == "MissileT2" || subtype == "MissileT3" || subtype == "MissileT4" || subtype == "MissileT5";
        }

        private void OnMissileRemoved(IMyMissile missile)
        {
            if (missile.AmmoDefinition.Id.SubtypeName == "Flare")
            {
                if (flares.ContainsKey(missile.EntityId))
                {
                    flares.Remove(missile.EntityId);
                }
            }

            MissileGuider missileGuider;

            if (missileGuiders.TryGetValue(missile.EntityId, out missileGuider))
            {
                missileGuider.effect?.Stop(true);
                missileGuiders.Remove(missile.EntityId);
            }
        }

        public bool CheckOwnership(IMyCubeGrid detectedGrid, long playerId)
        {
            if (detectedGrid.BigOwners.Count > 0)
            {
                if (detectedGrid.BigOwners[0] == playerId)
                {
                    return true;
                }

                IMyFaction gridOwnerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(detectedGrid.BigOwners[0]);

                if (gridOwnerFaction != null)
                {
                    if (gridOwnerFaction.IsMember(playerId))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void MissileTracking()
        {
            delay++;
            if (delay >= 60)
            {
                IMyCharacter localPlayer = MyAPIGateway.Session?.Player?.Character;

                if (localPlayer != null)
                {
                    IMyMissileGunObject rocketLauncher = localPlayer.EquippedTool as IMyMissileGunObject;
                    
                    if (rocketLauncher != null)
                    {
                        if ((rocketLauncher.DefinitionId == MyVisualScriptLogicProvider.GetDefinitionId("MyObjectBuilder_AutomaticRifle", "BasicHandHeldLauncherGun")) ||
                            (rocketLauncher.DefinitionId == MyVisualScriptLogicProvider.GetDefinitionId("MyObjectBuilder_AutomaticRifle", "AdvancedHandHeldLauncherGun")))
                        {
                            if (MyAPIGateway.Input.IsRightMousePressed())
                            {
                                if (sentToServer)
                                {
                                    if (rocketLauncherTrackers.ContainsKey(MyAPIGateway.Session.LocalHumanPlayer.IdentityId))
                                    {
                                        targetLocked = true;
                                    }

                                    else
                                    {
                                        MyAPIGateway.Utilities.ShowNotification("Travando no Alvo...", 16, "Red");
                                        return;
                                    }
                                }

                                if (targetLocked)
                                {
                                    MyAPIGateway.Utilities.ShowNotification("Alvo Travado!", 960, "Green");
                                    delay = 0;
                                    return;
                                }

                                var rayStart = MyAPIGateway.Session.Player.Character.GetHeadMatrix(false, true).Translation;
                                var rayEnd = rayStart + MyAPIGateway.Session.Player.Character.GetHeadMatrix(false, true).Forward * RAYCAST_DISTANCE;
                                List<IHitInfo> hitList = new List<IHitInfo>();

                                MyAPIGateway.Physics.CastRay(rayStart, rayEnd, hitList);

                                if (hitList.Count > 0)
                                {
                                    IMyEntity hitEntity = hitList[0].HitEntity;
                                    IMyCubeGrid detectedGrid = hitEntity as IMyCubeGrid;

                                    if (detectedGrid != null)
                                    {
                                        if (CheckOwnership(detectedGrid, MyAPIGateway.Session.LocalHumanPlayer.IdentityId))
                                        {
                                            MyAPIGateway.Utilities.ShowNotification("O Alvo é Aliado!", 16, "Red");
                                            return;
                                        }

                                        if (!sentToServer)
                                        {
                                            TrackerPacket.Setup(detectedGrid.EntityId, false);
                                            Net.SendToServer(TrackerPacket);
                                            sentToServer = true;
                                            return;
                                        }
                                    }
                                }

                                MyAPIGateway.Utilities.ShowNotification("Buscando o alvo!", 16, "Red");
                                return;
                            }

                            if (targetLocked)
                            {
                                TrackerPacket.Setup(0, true);
                                Net.SendToServer(TrackerPacket);
                                targetLocked = false;
                                sentToServer = false;
                                MyAPIGateway.Utilities.ShowNotification("Aguarde!", 2880, "Red");
                                delay = -120;
                                return;
                            }

                            MyAPIGateway.Utilities.ShowNotification("Segure o botão direito do mouse para travar no alvo!", 16, "Red");
                            return;
                        }
                    }
                }

                delay = 0;
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_npcTrackerHooked && _MESApi != null && _MESApi.MESApiReady)
            {
                _MESApi.BehaviorTriggerActivationWatcher(true, GetNPCTarget);
                _npcTrackerHooked = true;
            }

            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                MissileTracking();
            }

            foreach (var missileGuider in missileGuiders)
            {
                missileGuider.Value.Update();
            }

            if (delay100 >= 100)
            {
                lock (MissileTurret.missilesOnTarget)
                {
                    if (MissileTurret.missilesOnTarget.Any())
                    {
                        MissileTurret.missilesOnTarget.RemoveAll(missileTarget => missileTarget.missile == null || missileTarget.missile.MarkedForClose);
                    }
                }

                if (flares.Any())
                {
                    foreach (var pair in flares)
                    {
                        pair.Value.life++;
                        
                        if (pair.Value.life >= 4)
                        {
                            pair.Value.flare.Close();
                        }
                    }
                }

                delay100 = 0;
            }

            delay100++;
        }
    }
}
