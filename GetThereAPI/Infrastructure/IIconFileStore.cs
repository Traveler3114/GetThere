namespace GetThereAPI.Infrastructure;

public interface IIconFileStore
{
    bool Exists(string filename);
}
