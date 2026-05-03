namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public interface IEntityCanonicalTextRenderer<TFields>
{
    string Render(string name, TFields fields);
}
