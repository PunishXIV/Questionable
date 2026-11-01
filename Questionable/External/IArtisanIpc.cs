namespace Questionable.External;

internal interface IArtisanIpc
{
    ArtisanStatus GetStatus();
    ArtisanStartResult CraftList(int listId);
    ArtisanStartResult CraftItem(ushort recipeId, int crafts);
}
