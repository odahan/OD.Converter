namespace OD.Converter.Services;

public interface IArgumentBuilder
{
    List<string> BuildArguments(Models.ConversionOptions opts);
}