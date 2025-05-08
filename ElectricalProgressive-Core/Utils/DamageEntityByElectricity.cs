using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    public class DamageEntityByElectricity
    {
        //урон в зависимости от напряжения в проводе
        public static readonly Dictionary<int, float> DAMAGE_AMOUNT = new Dictionary<int, float>
        {
            { 32,  0.1f },
            { 128,  0.5f }
        };

        //сила отталкивания
        private const double KNOCKBACK_STRENGTH = 0.4;

        // Интервал в миллисекундах (2 секунды)
        private const long DAMAGE_INTERVAL_MS = 2000;

        // Ключ для хранения времени удара
        private const string key = "damageByElectricity";

        private ICoreAPI api;

        public global::ElectricalProgressive.ElectricalProgressive? System =>
             this.api?.ModLoader.GetModSystem<global::ElectricalProgressive.ElectricalProgressive>();



        /// <summary>
        /// Конструктор класса
        /// </summary>
        /// <param name="api"></param>
        public DamageEntityByElectricity(ICoreAPI api)
        {
            this.api = api;
        }


        /// <summary>
        /// Наносим  урон сущности
        /// </summary>
        /// <param name="world"></param>
        /// <param name="entity"></param>
        /// <param name="pos"></param>
        /// <param name="facing"></param>
        /// <param name="blockentity"></param>
        /// <param name="block"></param>
        public void Damage(IWorldAccessor world, Entity entity, BlockPos pos, BlockFacing facing, EParams[] AllEparams, Block block)
        {

            bool doDamage = false;
            int voltage=0;

            for (int i = 0; i <= 5; i++) //перебор всех граней
            {
                var networkInformation = this.System?.GetNetworks(pos, FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(i)));      //получаем информацию о сети

                if (networkInformation?.NumberOfProducers > 0 || networkInformation?.NumberOfAccumulators > 0) //если в сети есть генераторы или аккумы
                {
                    if (AllEparams != null) //энтити существует?
                    {
                        var par = AllEparams[i];
                        if (!par.burnout)           //не сгорел?
                        {
                            if (!par.isolated)      //не изолированный?
                            {
                                doDamage = true;   //значит урон разрешаем
                                if (par.voltage>voltage)  //запишем самый большой вольтаж
                                    voltage = par.voltage;
                            }
                        }
                    }
                }
            }


            if (!doDamage)
                return;

            // Текущее время в миллисекундах с запуска сервера
            long now = world.ElapsedMilliseconds;
            double last = entity.Attributes.GetDouble(key);

            if (last > now) 
                last = 0;

            // Если прошло >= 2 секунд, наносим урон и сбрасываем таймер
            if (now - last >= DAMAGE_INTERVAL_MS)
            {
                // 1) Наносим урон
                var dmg = new DamageSource()
                {
                    Source = EnumDamageSource.Block,
                    SourceBlock = block,
                    Type = EnumDamageType.Electricity,
                    SourcePos = pos.ToVec3d()
                };
                entity.ReceiveDamage(dmg, DAMAGE_AMOUNT[voltage]);

                // 2) Вычисляем вектор от блока к сущности и отталкиваем
                Vec3d center = pos.ToVec3d().Add(0.5, 0.5, 0.5);
                Vec3d diff = entity.ServerPos.XYZ - center;
                diff.Y = 0.2; // небольшой подъём
                diff.Normalize();

                entity.Attributes.SetDouble("kbdirX", diff.X * KNOCKBACK_STRENGTH);
                entity.Attributes.SetDouble("kbdirY", diff.Y * KNOCKBACK_STRENGTH);
                entity.Attributes.SetDouble("kbdirZ", diff.Z * KNOCKBACK_STRENGTH);

                // 3) Запоминаем время удара
                entity.Attributes.SetDouble(key, now);

                //рисуем искры
                ParticleManager.SpawnElectricSparks(entity.World, entity.Pos.XYZ);
            }
        }
    }
}
