using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Questionable.Model.Questing.Converter;

public sealed class QuestWorkConfigConverter : JsonConverter<QuestWorkValue>
{
    public override QuestWorkValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new QuestWorkValue(reader.GetByte());

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        byte? high = null, low = null;
        EQuestWorkMode mode = EQuestWorkMode.Bitwise;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    string? propertyName = reader.GetString();
                    if (propertyName == null || !reader.Read())
                        throw new JsonException();

                    switch (propertyName)
                    {
                        case nameof(QuestWorkValue.High):
                            high = reader.GetByte();
                            break;

                        case nameof(QuestWorkValue.Low):
                            low = reader.GetByte();
                            break;

                        case nameof(QuestWorkValue.Mode):
                            mode = new QuestWorkModeConverter().Read(ref reader, typeof(EQuestWorkMode), options);
                            break;

                        default:
                            throw new JsonException();
                    }

                    break;

                case JsonTokenType.EndObject:
                    return new QuestWorkValue(high, low, mode);

                default:
                    throw new JsonException();
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, QuestWorkValue value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
