using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallMissileLauncher), false, new string[] { "LargeMissileLauncherIronDome" })]
    public class MissileLauncher : MyGameLogicComponent
    {
        private IMySmallMissileLauncher missileLauncher;
        private IMyUserControllableGun gunBlock;
        private IMyGunObject<MyGunBase> gunObject;
        protected MyResourceSinkComponent resourceSink;
        private float launcherIdlePower = 25f;
        protected static readonly MyDefinitionId ElectricityId = new MyDefinitionId(typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_GasProperties), "Electricity");
        private List<IMyMissile> detectedMissiles = new List<IMyMissile>();
        public static Dictionary<long, IMyMissile> missileLauncherTarget = new Dictionary<long, IMyMissile>();
        private int delay = 0;
        private bool missileTracked = false;
        private IMyMissile lastTarget = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            missileLauncher = Entity as IMySmallMissileLauncher;
            gunBlock = Entity as IMyUserControllableGun;
            gunObject = gunBlock as IMyGunObject<MyGunBase>;
            resourceSink = gunBlock.ResourceSink as MyResourceSinkComponent;

            if (missileLauncher != null && !missileLauncher.MarkedForClose)
            {
                NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        private bool MissileIsEnemy(IMyMissile missile)
        {
            if (missile != null)
            {
                if (missile.Owner == missileLauncher.OwnerId)
                {
                    return false;
                }

                IMyFaction missileOwnerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(missile.Owner);

                if (missileOwnerFaction != null)
                {
                    if (missileOwnerFaction.IsMember(missileLauncher.OwnerId))
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
            BoundingSphereD turretRange = new BoundingSphereD(missileLauncher.GetPosition(), 3000);
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

                    if (MissileIsEnemy(missile))
                    {
                        detectedMissiles.Add(missile);
                    }
                }
            }
        }

        private void TurretFire(IMyMissile missile)
        {
            lock (missileLauncherTarget)
            {
                if (missileLauncherTarget.ContainsKey(missileLauncher.EntityId))
                {
                    missileLauncherTarget[missileLauncher.EntityId] = missile;
                }

                else
                {
                    missileLauncherTarget.Add(missileLauncher.EntityId, missile);
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

            return launcherIdlePower;
        }

        public override void UpdateBeforeSimulation10()
        {
            base.UpdateBeforeSimulation10();

            if (missileLauncher == null || !missileLauncher.IsWorking)
            {
                return;
            }

            if (delay >= 3)
            {
                TurretTracking();
                delay = 0;
            }

            if (MyAPIGateway.Session.IsServer && missileTracked)
            {
                missileLauncher.ShootOnce();
                missileTracked = false;
                delay++;
                return;
            }

            else if (missileTracked)
            {
                int i;
                bool targetInList = false;

                for (i = 0; i < MissileTurret.missilesOnTarget.Count(); i++)
                {
                    if (MissileTurret.missilesOnTarget[i].launcherId == missileLauncher.EntityId && MissileTurret.missilesOnTarget[i].missile == lastTarget)
                    {
                        targetInList = true;

                        if (MissileTurret.missilesOnTarget[i].inIntercept)
                        {
                            missileTracked = false;
                            break;
                        }

                        MissileTurret.missilesOnTarget[i].time++;

                        if (MissileTurret.missilesOnTarget[i].time >= 33)
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

            if (detectedMissiles.Any())
            {
                foreach (var detectedMissile in detectedMissiles)
                {
                    bool onTarget = false;

                    lock (MissileTurret.missilesOnTarget)
                    {
                        if (MissileTurret.missilesOnTarget.Any())
                        {
                            int i;

                            for (i = 0; i < MissileTurret.missilesOnTarget.Count(); i++)
                            {
                                if (MissileTurret.missilesOnTarget[i].missile == detectedMissile)
                                {
                                    onTarget = true;
                                    break;
                                }
                            }

                            if (onTarget)
                            {
                                if (!MissileTurret.missilesOnTarget[i].inIntercept && MissileTurret.missilesOnTarget[i].missile != lastTarget && MissileTurret.missilesOnTarget[i].time >= 1)
                                {
                                    if (MissileTurret.missilesOnTarget[i].missile != null && !MissileTurret.missilesOnTarget[i].missile.MarkedForClose)
                                    {
                                        TurretFire(detectedMissile);
                                        lastTarget = detectedMissile;
                                        MissileTurret.missilesOnTarget[i].launcherId = missileLauncher.EntityId;
                                        MissileTurret.missilesOnTarget[i].time = 0;
                                        break;
                                    }
                                }
                            }
                        }

                        if (!onTarget && detectedMissile != null && !detectedMissile.MarkedForClose && detectedMissile != lastTarget)
                        {
                            TurretFire(detectedMissile);
                            lastTarget = detectedMissile;
                            MissileTurret.missilesOnTarget.Add(new MissileTurret.MissileTarget(true, detectedMissile, missileLauncher.EntityId, 0));
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

            if (missileLauncher != null && resourceSink != null)
            {
                resourceSink.SetMaxRequiredInputByType(ElectricityId, launcherIdlePower);
                resourceSink.SetRequiredInputFuncByType(ElectricityId, GetCurrentPowerUsage);
                resourceSink.Update();
            }
        }
    }
}
