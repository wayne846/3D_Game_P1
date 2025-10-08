
public class Interval
{
    public float min;
    public float max;

    public float size { get { return max - min; } }

    public Interval()
    {
        this.min = float.NegativeInfinity;
        this.max = float.PositiveInfinity;
    }

    public Interval(float min, float max)
    {
        this.min = min;
        this.max = max;
    }

    public bool Contains(float x) {
        return (min <= x && x <= max);
    }

    public bool Surrounds(float x) {
        return (min < x && x < max);
    }

    public static readonly Interval Empty = new Interval(float.PositiveInfinity, float.NegativeInfinity);
    public static readonly Interval Universe = new Interval(float.NegativeInfinity, float.PositiveInfinity);
};
