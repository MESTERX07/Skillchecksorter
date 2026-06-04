namespace SkillCheckSorter;

public record AppSettings(int SourceFolder, int HitFolder, int MissFolder)
{
    public static AppSettings Validated(int source, int hit, int miss)
    {
        static int Clamp(int v) => Math.Clamp(v, 0, 9);
        return new AppSettings(Clamp(source), Clamp(hit), Clamp(miss));
    }
}
