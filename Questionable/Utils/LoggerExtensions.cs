using System.Runtime.CompilerServices;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using Questionable.Controller;

namespace Questionable.Utils;
internal static class LoggerExtensions
{
    internal static string LogChat(this ILogger logger, IChatGui chatGui, string category, string? message = null)
    {
        string output = $"{category}{(message != null ? $": {message}" : "")}";
        chatGui.Print(output, CommandHandler.MessageTag, CommandHandler.TagColor);
        logger.LogInformation(output);
        return message ?? category;
    }
    internal static string LogChatError(this ILogger logger, IChatGui chatGui, string category, string message)
    {
        chatGui.PrintError($"{category}: {message}", CommandHandler.MessageTag, CommandHandler.TagColor);
        logger.LogWarning($"{category}: {message}");
        return message;
    }
}
