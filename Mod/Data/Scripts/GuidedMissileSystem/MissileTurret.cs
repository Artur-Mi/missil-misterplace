using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Cython.GuidedMissiles
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_LargeMissileTurret), false, new string[] {"LargeMissileTurretIronDome"})]
    public class MissileTurret : MyGameLogicComponent
    {
        private IMyLargeMissileTurret missileTurret;
        private IMyUserControllableGun gunBlock;
        private IMyGunObject<MyGunBase> gunObject;
        protected MyResourceSinkComponent resourceSink;
        private float turretIdlePower = 25f;
        protected static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");
        private List<IMyMissile> detectedMissiles = new List<IMyMissile>();
        public static Dictionary<long, IMyMissile> missileTurretTarget = new Dictionary<long, IMyMissile>();
        public static List<MissileTarget> missilesOnTarget = new List<MissileTarget>();
        private uint delay = 0;
        private bool missileTracked = false;
        private IMyMissile lastTarget = null;
        public static readonly HashSet<string> missilesSubtypeIdWhitelist = new HashSet<string>()
        {
            "Missile",
            "Torpedo",          // vanilla torpedoes
            "Torpedo_Heavy",
            "Torpedo_Seeking",
            "Torpedo_Piercing",
            "MissileHoming",  // vanilla homing rocket (Missile200mmHoming; the advanced hand-held launcher fires it)
            "PMissike",
            "AMissile",
            "LauncherXMissile",
            "MissileT2",
            "MissileT3",
            "MissileT4",
            "MissileT5",
            "CEC_ArtilleryRocket_BP"
        };

        public class MissileTarget
        {
            public bool inIntercept;
            public IMyMissile missile;
            public long launcherId;
            public int time;

            public MissileTarget(bool inIntercept, IMyMissile missile, long launcherId, int time)
            {
                this.inIntercept = inIntercept;
                this.missile = missile;
                this.launcherId = launcherId;
                this.time = time;
            }
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            missileTurret = Entity as IMyLargeMissileTurret;
            gunBlock = Entity as IMyUserControllableGun;
            gunObject = gunBlock as IMyGunObject<MyGunBase>;
            resourceSink = gunBlock.ResourceSink as MyResourceSinkComponent;

            if (missileTurret != null && !missileTurret.MarkedForClose)
            {
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        // Diagnostics: logs once per ammo subtype every missile an Iron Dome sees but does not list, so a renamed or
        // new missile shows up in the game log as "[IronDome] not in whitelist: <subtype>".
        private static readonly HashSet<string> unlistedLogged = new HashSet<string>();

        public static void NoteUnlistedMissile(IMyMissile missile)
        {
            string subtype = missile?.AmmoDefinition.Id.SubtypeName;

            if (subtype == null || missilesSubtypeIdWhitelist.Contains(subtype) || !unlistedLogged.Add(subtype))
            {
                return;
            }

            VRage.Utils.MyLog.Default.WriteLineAndConsole("[IronDome] not in whitelist: " + subtype);
        }

        public static bool MissileIsEnemy(IMyMissile missile, long playerId)
        {
            if (missile != null)
            {
                if (missile.Owner == playerId)
                {
                    return false;
                }

                IMyFaction missileOwnerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(missile.Owner);

                if (missileOwnerFaction != null)
                {
                    if (missileOwnerFaction.IsMember(playerId))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        private void TurretTracking()
        {
            BoundingSphereD turretRange = new BoundingSphereD(missileTurret.GetPosition(), missileTurret.Range);
            List<MyEntity> missilesInRange = new List<MyEntity>();
            MyAPIGateway.Missiles.GetAllMissilesInSphere(ref turretRange, missilesInRange);
            detectedMissiles.Clear();

            if (!missilesInRange.Any())
            {
                return;
            }

            foreach (MyEntity missileEntity in missilesInRange)
            {
                if (missileEntity != null && !missileEntity.MarkedForClose)
                {
                    IMyMissile missile = missileEntity as IMyMissile;

                    NoteUnlistedMissile(missile);

                    if (missilesSubtypeIdWhitelist.Contains(missile?.AmmoDefinition.Id.SubtypeName) && MissileIsEnemy(missile, missileTurret.OwnerId))
                    {
                        if (GuidedMissileSystem.IsAquaticAmmo(missile.AmmoDefinition.Id.SubtypeName))
                        {
                            GuidedMissileSystem.MissileGuider missileGuider;
                            GuidedMissileSystem.missileGuiders.TryGetValue(missile.EntityId, out missileGuider);

                            if (missileGuider != null && missileGuider.isUnderwater)
                            {
                                continue;
                            }
                        }

                        detectedMissiles.Add(missile);
                    }
                }
            }
        }

        private bool TheViewIsClear(IMyMissile missile)
        {
            bool isClear = true;
            var rayStart = missileTurret.GetPosition();
            var rayEnd = missile.GetPosition();
            List<IHitInfo> hitList = new List<IHitInfo>();

            MyAPIGateway.Physics.CastRay(rayStart, rayEnd, hitList);

            if (hitList.Any())
            {
                foreach (var hitInfo in hitList)
                {
                    IMyEntity entity = MyAPIGateway.Entities.GetEntityById(hitInfo.HitEntity.EntityId);

                    if (entity != null)
                    {
                        IMyLargeMissileTurret missileTurretInPath = entity as IMyLargeMissileTurret;

                        if (missileTurretInPath != null)
                        {
                            if (missileTurretInPath == missileTurret)
                            {
                                continue;
                            }
                        }

                        IMyMissile missileInPath = entity as IMyMissile;

                        if (missileInPath != null)
                        {
                            continue;
                        }
                    }

                    isClear = false;
                    break;
                }
            }

            return isClear;
        }

        private void TurretFire(IMyMissile missile)
        {
            missileTurret.TrackTarget(missile);

            lock (missileTurretTarget)
            {
                if (missileTurretTarget.ContainsKey(missileTurret.EntityId))
                {
                    missileTurretTarget[missileTurret.EntityId] = missile;
                }

                else
                {
                    missileTurretTarget.Add(missileTurret.EntityId, missile);
                }
            }

            missileTracked = true;
        }

        protected virtual float GetCurrentPowerUsage()
        {

            if (!gunBlock.IsFunctional)
                return 0;

            if (!gunBlock.Enabled)
                return 0;

            turretIdlePower = 25f * (missileTurret.Range / 3000f);

            if (turretIdlePower < 0.05f)
            {
                turretIdlePower = 0.05f;
            }

            return turretIdlePower;
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            missileTurret.TargetCharacters = false;
            missileTurret.TargetEnemies = false;
            missileTurret.TargetLargeGrids = false;
            missileTurret.TargetMeteors = false;
            missileTurret.TargetMissiles = false;
            missileTurret.TargetNeutrals = false;
            missileTurret.TargetSmallGrids = false;
            missileTurret.TargetStations = false;
            missileTurret.EnableFirstPersonView = false;
            missileTurret.EnableIdleRotation = false;
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();

            if (missileTurret == null || !missileTurret.IsWorking)
            {
                return;
            }

            if (delay >= 4)
            {
                TurretTracking();
                delay = 0;
            }

            if (MyAPIGateway.Session.IsServer && missileTracked)
            {
                missileTurret.ShootOnce();
                missileTracked = false;
                delay++;
                return;
            }

            else if (missileTracked)
            {
                int i;
                bool targetInList = false;

                for (i = 0; i < missilesOnTarget.Count(); i++)
                {
                    if (missilesOnTarget[i].launcherId == missileTurret.EntityId && missilesOnTarget[i].missile == lastTarget)
                    {
                        targetInList = true;

                        if (missilesOnTarget[i].inIntercept)
                        {
                            missileTracked = false;
                            break;
                        }

                        missilesOnTarget[i].time++;

                        if (missilesOnTarget[i].time >= 33)
                        {
                            missileTracked = false;
                        }

                        break;
                    }
                }

                if (targetInList)
                {
                    return;
                }

                missileTracked = false;
            }

            if (!missileTracked && detectedMissiles.Any())
            {
                foreach (var detectedMissile in detectedMissiles)
                {
                    if (!TheViewIsClear(detectedMissile))
                    {
                        continue;
                    }

                    bool onTarget = false;

                    lock (missilesOnTarget)
                    {
                        if (missilesOnTarget.Any())
                        {
                            int i;

                            for (i = 0; i < missilesOnTarget.Count(); i++)
                            {
                                if (missilesOnTarget[i].missile == detectedMissile)
                                {
                                    onTarget = true;
                                    break;
                                }
                            }

                            if (onTarget)
                            {
                                if (!missilesOnTarget[i].inIntercept && missilesOnTarget[i].missile != lastTarget && missilesOnTarget[i].time >= 1)
                                {
                                    if (missilesOnTarget[i].missile != null && !missilesOnTarget[i].missile.MarkedForClose)
                                    {
                                        TurretFire(detectedMissile);
                                        lastTarget = detectedMissile;
                                        missilesOnTarget[i].launcherId = missileTurret.EntityId;
                                        missilesOnTarget[i].time = 0;
                                        break;
                                    }
                                }
                            }
                        }

                        if (!onTarget && detectedMissile != null && !detectedMissile.MarkedForClose && detectedMissile != lastTarget)
                        {
                            TurretFire(detectedMissile);
                            lastTarget = detectedMissile;
                            missilesOnTarget.Add(new MissileTarget(true, detectedMissile, missileTurret.EntityId, 0));
                            break;
                        }
                    }
                }
            }

            delay++;
        }

        public override void UpdateBeforeSimulation100()
        {
            base.UpdateBeforeSimulation100();

            if (missileTurret != null && resourceSink != null)
            {
                resourceSink.SetMaxRequiredInputByType(ElectricityId, turretIdlePower);
                resourceSink.SetRequiredInputFuncByType(ElectricityId, GetCurrentPowerUsage);
                resourceSink.Update();
            }
        }
    }
}
