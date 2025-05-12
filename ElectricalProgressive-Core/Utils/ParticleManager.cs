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
            color: ColorUtil.ColorFromRgba(155, 255, 255, 83),
            minPos: new Vec3d(), maxPos: new Vec3d(0.1, 0.0, 0.1),
            minVelocity: new Vec3f(-4f, 0f, -4f), maxVelocity: new Vec3f(4f, 4f, 4f)
        )
        {
            Bounciness = 1f,                     // отскок частиц от блоков
            VertexFlags = 128,                // флаг для отрисовки частиц
            addLifeLength = 0.5f,                // время жизни частиц
            LifeLength = 0.5f,
            GravityEffect = 1.0f,
            ParticleModel = EnumParticleModel.Cube,
            MinSize = 0.4f,                              // маленький стартовый размер :contentReference[oaicite:1]{index=1}
            MaxSize = 0.6f,                              // без разброса размера :contentReference[oaicite:2]{index=2}
            LightEmission = 0,                        // яркость частиц

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
