using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using System.Collections.Generic;
using System.Linq;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Cython.GuidedMissiles
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallMissileLauncher), false, new string[] { "LargeMissileLauncherIronDome", "IronDomeX" })]
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

        // Scan range, set from the terminal slider. The default is the old fixed range, so blocks saved before the
        // slider existed behave as before. Saved in the block's mod storage and synced with RangePacket.
        public const float MaxRange = 3000f;
        public const float MinRange = 100f;
        public static readonly System.Guid RangeGuid = new System.Guid("B1D5A2C4-7E3F-4C58-9A61-2F0E8D14C7A9");
        private float range = MaxRange;
        private float antennaRange = 0f;
        private bool rangeReloaded = false;
        private static IMyTerminalControlSlider rangeSlider;
        private bool missileTracked = false;
        private IMyMissile lastTarget = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            missileLauncher = Entity as IMySmallMissileLauncher;
            gunBlock = Entity as IMyUserControllableGun;
            gunObject = gunBlock as IMyGunObject<MyGunBase>;
            resourceSink = gunBlock.ResourceSink as MyResourceSinkComponent;

            LoadRange();

            if (missileLauncher != null && !missileLauncher.MarkedForClose)
            {
                NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
        }

        private static float ClampRange(float value)
        {
            return MathHelper.Clamp((float)System.Math.Round(value), MinRange, MaxRange);
        }

        // True when the AntennaIronDome mod is loaded. Then an antenna is required: no working antenna on the grid means
        // no scanning and no firing, and the Range slider is hidden.
        private static bool? antennaModLoaded;

        public static bool AntennaModLoaded
        {
            get
            {
                if (antennaModLoaded == null)
                {
                    MyCubeBlockDefinition definition;
                    antennaModLoaded = MyDefinitionManager.Static.TryGetCubeBlockDefinition(new MyDefinitionId(typeof(MyObjectBuilder_RadioAntenna), "AntennaIronDome"), out definition);
                }

                return antennaModLoaded.Value;
            }
        }

        // Broadcast radius of the best working AntennaIronDome on the block's grid, or 0 when there is none. While an
        // antenna exists it replaces the slider range (the slider is the fallback).
        public static float GetAntennaRadius(IMyCubeBlock block)
        {
            if (block == null || block.CubeGrid == null)
            {
                return 0f;
            }

            float best = 0f;

            foreach (IMyRadioAntenna antenna in block.CubeGrid.GetFatBlocks<IMyRadioAntenna>())
            {
                if (antenna.BlockDefinition.SubtypeId == "AntennaIronDome" && antenna.IsWorking && antenna.Radius > best)
                {
                    best = antenna.Radius;
                }
            }

            // Never more than the missile's max trajectory (3000 m).
            return System.Math.Min(best, MaxRange);
        }

        // The scan range in use: the antenna's radius when the antenna mod is loaded (0 = no working antenna, so no
        // scanning), else the slider value.
        private float EffectiveRange
        {
            get { return AntennaModLoaded ? antennaRange : range; }
        }

        private void LoadRange()
        {
            if (Entity == null || Entity.Storage == null || !Entity.Storage.ContainsKey(RangeGuid))
            {
                return;
            }

            float saved;

            if (float.TryParse(Entity.Storage[RangeGuid], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out saved))
            {
                range = ClampRange(saved);
            }
        }

        public void SetRange(float value)
        {
            range = ClampRange(value);

            if (Entity != null)
            {
                if (Entity.Storage == null)
                {
                    Entity.Storage = new MyModStorageComponent();
                }

                Entity.Storage[RangeGuid] = range.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static MissileLauncher GetLogic(IMyTerminalBlock block)
        {
            return block?.GameLogic?.GetAs<MissileLauncher>();
        }

        // Called on every machine when a range packet arrives (and locally when the slider moves).
        public static void ApplyRange(long blockId, float value)
        {
            IMyEntity entity;

            if (MyAPIGateway.Entities.TryGetEntityById(blockId, out entity))
            {
                GetLogic(entity as IMyTerminalBlock)?.SetRange(value);
            }
        }

        // Adds the Range slider to the terminal of every small missile launcher; it is shown only for blocks that have
        // this logic (the Iron Dome launchers: LargeMissileLauncherIronDome and IronDomeX).
        public static void CreateControls()
        {
            if (rangeSlider != null || MyAPIGateway.TerminalControls == null)
            {
                return;
            }

            IMyTerminalControlSlider slider = MyAPIGateway.TerminalControls.CreateControl<IMyTerminalControlSlider, IMySmallMissileLauncher>("MisterIronDome_Range");
            slider.Title = MyStringId.GetOrCompute("Range");
            slider.Tooltip = MyStringId.GetOrCompute("Radius within which the Iron Dome looks for enemy missiles. A shorter range uses less power.");
            slider.SetLimits(MinRange, MaxRange);
            slider.SupportsMultipleBlocks = true;
            slider.Visible = block => GetLogic(block) != null && !AntennaModLoaded;
            slider.Enabled = block => true;
            slider.Getter = block =>
            {
                MissileLauncher logic = GetLogic(block);
                return logic != null ? logic.range : MaxRange;
            };
            slider.Setter = (block, value) =>
            {
                if (GetLogic(block) != null)
                {
                    GuidedMissileSystem.SendRange(block.EntityId, value);
                }
            };
            slider.Writer = (block, text) =>
            {
                MissileLauncher logic = GetLogic(block);
                text.Append((int)(logic != null ? logic.range : MaxRange)).Append(" m");
            };

            MyAPIGateway.TerminalControls.AddControl<IMySmallMissileLauncher>(slider);
            rangeSlider = slider;

            HideTurretRange();
        }

        // Vanilla controls of the classic turret that are always hidden: it does no targeting of its own and is not
        // manually controlled (Control button, idle movement, target locking, target options, focus, view rotation).
        // The Range slider is hidden only while the antenna mod is loaded (its range is the antenna's).
        private static readonly HashSet<string> hiddenTurretControlIds = new HashSet<string>()
        {
            "Control", "EnableIdleMovement", "EnableTargetLocking", "TargetingGroup_Selector", "FocusLockedTarget"
        };
        private static readonly Dictionary<IMyTerminalControl, System.Func<IMyTerminalBlock, bool>> hiddenTurretControls = new Dictionary<IMyTerminalControl, System.Func<IMyTerminalBlock, bool>>();

        private static void HideTurretRange()
        {
            // These may be registered on the turret base type, not on the missile turret type.
            List<IMyTerminalControl> controls = new List<IMyTerminalControl>();
            List<IMyTerminalControl> baseControls = new List<IMyTerminalControl>();
            MyAPIGateway.TerminalControls.GetControls<IMyLargeMissileTurret>(out controls);
            MyAPIGateway.TerminalControls.GetControls<IMyLargeTurretBase>(out baseControls);
            controls.AddRange(baseControls);

            System.Text.StringBuilder ids = new System.Text.StringBuilder();

            foreach (IMyTerminalControl control in controls)
            {
                ids.Append(control.Id).Append(' ');

                if (hiddenTurretControls.ContainsKey(control) || !(hiddenTurretControlIds.Contains(control.Id) || control.Id == "Range" || control.Id.IndexOf("Rotation", System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                System.Func<IMyTerminalBlock, bool> original = control.Visible;
                bool onlyWithAntenna = control.Id == "Range";
                hiddenTurretControls[control] = original;
                control.Visible = block =>
                    (original == null || original(block)) &&
                    !((AntennaModLoaded || !onlyWithAntenna) && block.BlockDefinition.SubtypeId == "LargeMissileTurretIronDome");
            }

            VRage.Utils.MyLog.Default.WriteLineAndConsole("[IronDome] antenna mod loaded: " + AntennaModLoaded + ", hidden turret controls: " + hiddenTurretControls.Count + ", controls: " + ids);
        }

        public static void RemoveControls()
        {
            if (rangeSlider != null && MyAPIGateway.TerminalControls != null)
            {
                MyAPIGateway.TerminalControls.RemoveControl<IMySmallMissileLauncher>(rangeSlider);
            }

            rangeSlider = null;

            foreach (KeyValuePair<IMyTerminalControl, System.Func<IMyTerminalBlock, bool>> pair in hiddenTurretControls)
            {
                pair.Key.Visible = pair.Value;
            }

            hiddenTurretControls.Clear();
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
            antennaRange = GetAntennaRadius(missileLauncher);
            detectedMissiles.Clear();

            if (EffectiveRange <= 0f)
            {
                return;
            }

            BoundingSphereD turretRange = new BoundingSphereD(missileLauncher.GetPosition(), EffectiveRange);
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

                    MissileTurret.NoteUnlistedMissile(missile);

                    if (MissileTurret.missilesSubtypeIdWhitelist.Contains(missile?.AmmoDefinition.Id.SubtypeName) && MissileIsEnemy(missile))
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

            if (!rangeReloaded)
            {
                // The block's mod storage may not be filled yet in Init, so read the saved range once more here.
                rangeReloaded = true;
                LoadRange();
            }

            if (missileLauncher != null && resourceSink != null)
            {
                antennaRange = GetAntennaRadius(missileLauncher);
                launcherIdlePower = 25f * (EffectiveRange / MaxRange);
                resourceSink.SetMaxRequiredInputByType(ElectricityId, launcherIdlePower);
                resourceSink.SetRequiredInputFuncByType(ElectricityId, GetCurrentPowerUsage);
                resourceSink.Update();
            }
        }
    }
}
