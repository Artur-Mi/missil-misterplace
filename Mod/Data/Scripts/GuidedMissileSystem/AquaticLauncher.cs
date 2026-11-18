using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace Cython.GuidedMissiles
{
    // Underwater-only launchers (SLBM and LauncherX): the block only works while its muzzle is under water.
    //
    // The Water Mod's own FunctionalAboveWater=false (WaterConfig.xml) is kept, but it tests one point of the block
    // (its origin cell) and does nothing at all where there is no water (space, dry planets, asteroids). This component
    // is stricter: it tests the muzzle, and with the Water Mod loaded it also switches the block off outside water.
    // Without the Water Mod it does nothing (we cannot tell where the water is).
    //
    // Runs on the server only; the block's Enabled state is synchronized to clients by the game.
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_SmallMissileLauncher), false, "LargeMissileLauncherSLBM", "LauncherX")]
    public class AquaticLauncher : MyGameLogicComponent
    {
        const int ConfirmChecks = 2;        // consecutive checks (10 frames each) before changing state, to ride out waves
        const float MuzzleInset = 0.5f;     // metres inside the front face where the muzzle is sampled

        IMyFunctionalBlock _block;
        bool _heldOff;                      // we switched the block off
        bool _restoreOn;                    // it was on (or the player tried to switch it on) while we held it off
        int _pending;                       // consecutive checks that disagree with the current state

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            _block = Entity as IMyFunctionalBlock;
            if (_block == null || _block.CubeGrid == null || _block.CubeGrid.Physics == null) return;   // not a real block (preview/projection)
            if (!MyAPIGateway.Session.IsServer) return;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateBeforeSimulation10()
        {
            if (_block == null || _block.MarkedForClose || _block.Closed) return;
            if (!Jakaria.API.WaterModAPI.Registered) return;

            bool underwater = Jakaria.API.WaterModAPI.IsUnderwater(MuzzlePosition());

            if (!underwater)
            {
                if (_block.Enabled)
                {
                    if (++_pending >= ConfirmChecks)
                    {
                        _heldOff = true;
                        _restoreOn = true;
                        _block.Enabled = false;
                        _pending = 0;
                    }
                }
                else
                {
                    _pending = 0;
                }
            }
            else
            {
                if (_heldOff)
                {
                    if (++_pending >= ConfirmChecks)
                    {
                        _block.Enabled = _restoreOn;
                        _heldOff = false;
                        _restoreOn = false;
                        _pending = 0;
                    }
                }
                else
                {
                    _pending = 0;
                }
            }
        }

        // Front face of the block (forward is -Z), a little inside.
        Vector3D MuzzlePosition()
        {
            BoundingBox box = _block.LocalAABB;
            Vector3 front = new Vector3(box.Center.X, box.Center.Y, box.Min.Z + MuzzleInset);
            return Vector3D.Transform(front, _block.WorldMatrix);
        }
    }
}
