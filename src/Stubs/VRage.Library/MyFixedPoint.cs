namespace VRage;

public readonly struct MyFixedPoint
{
    private readonly long _raw;

    public static explicit operator MyFixedPoint(double value) => default;
    public static explicit operator double(MyFixedPoint value) => default;
}
