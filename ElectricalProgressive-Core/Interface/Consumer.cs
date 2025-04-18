using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Interface;

public interface IElectricConsumer
{
    /// <summary>
    /// ���������� ������������
    /// </summary>
    public BlockPos Pos { get; }
    /// <summary>
    /// ������� ����������� � ����������� ������� �� ����� � ������ ������ �������
    /// </summary>
    public float Consume_request();

    /// <summary>
    /// ������� ������ ������� ����������� 
    /// </summary>
    public void Consume_receive(float amount);


    /// <summary>
    /// ��������� Entity
    /// </summary>
    public void Update();


    /// <summary>
    /// ������� �������� � ������ ������ �����������
    /// </summary>
    /// <returns></returns>
    public float getPowerReceive();


    /// <summary>
    /// ������� ������� � ������ ������ �����������
    /// </summary>
    /// <returns></returns>
    public float getPowerRequest();



}
