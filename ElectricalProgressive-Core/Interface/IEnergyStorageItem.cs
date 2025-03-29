using Vintagestory.API.Common;

namespace ElectricalProgressive.Interface;

public interface IEnergyStorageItem
{
    int receiveEnergy(ItemStack itemstack, int maxReceive);
}