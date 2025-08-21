using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.String;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.Logging;
using Questionable.Model.Questing;

namespace Questionable.Functions;

internal sealed unsafe class ChatFunctions
{
    private delegate void ProcessChatBoxDelegate(IntPtr uiModule, IntPtr message, IntPtr unused, byte a4);

    private readonly ReadOnlyDictionary<EEmote, string> _emoteCommands;

    private readonly GameFunctions _gameFunctions;
    private readonly ITargetManager _targetManager;
    private readonly ILogger<ChatFunctions> _logger;
    private readonly ProcessChatBoxDelegate _processChatBox;

    public ChatFunctions(ISigScanner sigScanner, IDataManager dataManager, GameFunctions gameFunctions,
        ITargetManager targetManager, ILogger<ChatFunctions> logger)
    {
        _gameFunctions = gameFunctions;
        _targetManager = targetManager;
        _logger = logger;
        _processChatBox =
            Marshal.GetDelegateForFunctionPointer<ProcessChatBoxDelegate>(sigScanner.ScanText(Signatures.SendChat));

        _emoteCommands = dataManager.GetExcelSheet<Emote>()
            .Where(x => x.RowId > 0)
            .Where(x => x.TextCommand.IsValid)
            .Select(x => (x.RowId, Command: x.TextCommand.Value.Command.ToString()))
            .Where(x => !string.IsNullOrEmpty(x.Command) && x.Command.StartsWith('/'))
            .ToDictionary(x => (EEmote)x.RowId, x => x.Command)
            .AsReadOnly();
    }

    /// <summary>
    /// <para>
    /// Send a given message to the chat box. <b>This can send chat to the server.</b>
    /// </para>
    /// <para>
    /// <b>This method is unsafe.</b> This method does no checking on your input and
    /// may send content to the server that the normal client could not. You must
    /// verify what you're sending and handle content and length to properly use
    /// this.
    /// </para>
    /// </summary>
    /// <param name="message">Message to send</param>
    /// <exception cref="InvalidOperationException">If the signature for this function could not be found</exception>
    private void SendMessageUnsafe(byte[] message)
    {
        var uiModule = (IntPtr)Framework.Instance()->GetUIModule();

        using var payload = new ChatPayload(message);
        var mem1 = Marshal.AllocHGlobal(400);
        Marshal.StructureToPtr(payload, mem1, false);

        _processChatBox(uiModule, mem1, IntPtr.Zero, 0);

        Marshal.FreeHGlobal(mem1);
    }

    /// <summary>
    /// <para>
    /// Send a given message to the chat box. <b>This can send chat to the server.</b>
    /// </para>
    /// <para>
    /// This method is slightly less unsafe than <see cref="SendMessageUnsafe"/>. It
    /// will throw exceptions for certain inputs that the client can't normally send,
    /// but it is still possible to make mistakes. Use with caution.
    /// </para>
    /// </summary>
    /// <param name="message">message to send</param>
    /// <exception cref="ArgumentException">If <paramref name="message"/> is empty, longer than 500 bytes in UTF-8, or contains invalid characters.</exception>
    /// <exception cref="InvalidOperationException">If the signature for this function could not be found</exception>
    private void SendMessage(string message)
    {
        _logger.LogDebug("Attempting to send chat message '{Message}'", message);
        var bytes = Encoding.UTF8.GetBytes(message);
        if (bytes.Length == 0)
            throw new ArgumentException("message is empty", nameof(message));

        if (bytes.Length > 500)
            throw new ArgumentException("message is longer than 500 bytes", nameof(message));

        if (message.Length != SanitiseText(message).Length)
            throw new ArgumentException("message contained invalid characters", nameof(message));

        SendMessageUnsafe(bytes);
    }

    /// <summary>
    /// <para>
    /// Sanitises a string by removing any invalid input.
    /// </para>
    /// <para>
    /// The result of this method is safe to use with
    /// <see cref="SendMessage"/>, provided that it is not empty or too
    /// long.
    /// </para>
    /// </summary>
    /// <param name="text">text to sanitise</param>
    /// <returns>sanitised text</returns>
    /// <exception cref="InvalidOperationException">If the signature for this function could not be found</exception>
    private string SanitiseText(string text)
    {
        var uText = Utf8String.FromString(text);

        uText->SanitizeString((AllowedEntities)0x27F);
        var sanitised = uText->ToString();

        uText->Dtor();
        IMemorySpace.Free(uText);

        return sanitised;
    }

    public void ExecuteCommand(string command)
    {
        if (!command.StartsWith('/'))
            return;

        SendMessage(command);
    }

    public void UseEmote(uint dataId, EEmote emote)
    {
        IGameObject? gameObject = _gameFunctions.FindObjectByDataId(dataId);
        if (gameObject != null)
        {
            _targetManager.Target = gameObject;
            ExecuteCommand($"{_emoteCommands[emote]} motion");
        }
    }

    public void UseEmote(EEmote emote)
    {
        ExecuteCommand($"{_emoteCommands[emote]} motion");
    }

    private static class Signatures
    {
        internal const string SendChat = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9";
    }

    [StructLayout(LayoutKind.Explicit)]
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    private readonly struct ChatPayload : IDisposable
    {
        [FieldOffset(0)] private readonly IntPtr textPtr;

        [FieldOffset(16)] private readonly ulong textLen;

        [FieldOffset(8)] private readonly ulong unk1;

        [FieldOffset(24)] private readonly ulong unk2;

        internal ChatPayload(byte[] stringBytes)
        {
            textPtr = Marshal.AllocHGlobal(stringBytes.Length + 30);
            Marshal.Copy(stringBytes, 0, textPtr, stringBytes.Length);
            Marshal.WriteByte(textPtr + stringBytes.Length, 0);

            textLen = (ulong)(stringBytes.Length + 1);

            unk1 = 64;
            unk2 = 0;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(textPtr);
        }
    }
}
