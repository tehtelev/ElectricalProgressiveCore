using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    public static class ParticleManager
    {
        // шаблон «электрических искр»
        private static readonly SimpleParticleProperties SparksTemplate = new SimpleParticleProperties(
            minQuantity: 5, maxQuantity: 10,
            color: ColorUtil.ColorFromRgba(255, 255, 255, 255),
            minPos: new Vec3d(), maxPos: new Vec3d(0.1, 0.0, 0.1),
            minVelocity: new Vec3f(-2f, 0f, -2f), maxVelocity: new Vec3f(2f, 4f, 2f)
        )
        {
            
            LifeLength = 0.5f,
            GravityEffect = 0.9f,
            ParticleModel = EnumParticleModel.Cube,
            MinSize = 1.0f,                              // маленький стартовый размер :contentReference[oaicite:1]{index=1}
            MaxSize = 1.0f,                              // без разброса размера :contentReference[oaicite:2]{index=2}
            LightEmission = 10,                        // яркость частиц
            OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -255)
        };

        // шаблон «чёрного дыма»
        private static readonly SimpleParticleProperties SmokeTemplate = new SimpleParticleProperties(
            minQuantity: 1, maxQuantity: 1,
            color: ColorUtil.ColorFromRgba(50, 50, 50, 200),
            minPos: new Vec3d(), maxPos: new Vec3d(0.3, 0.1, 0.3),
            minVelocity: new Vec3f(0f, 0.1f, 0f), maxVelocity: new Vec3f(0.2f, 0.2f, 0.2f)
        )
        {
            WindAffected = true,
            LifeLength = 2f,
            GravityEffect = -0.02f,
            ParticleModel = EnumParticleModel.Quad,
            SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1f),
            OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -100)
        };

        // метод для спавна искр в точке pos
        public static void SpawnElectricSparks(IWorldAccessor world, Vec3d pos)
        {
            SparksTemplate.MinPos = pos;
            world.SpawnParticles(SparksTemplate);
        }

        // метод для спавна дыма в точке pos
        public static void SpawnBlackSmoke(IWorldAccessor world, Vec3d pos)
        {
            SmokeTemplate.MinPos = pos;
            world.SpawnParticles(SmokeTemplate);
        }
    }
}
