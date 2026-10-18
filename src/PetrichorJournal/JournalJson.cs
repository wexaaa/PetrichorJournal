using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace PetrichorJournal;

public static class JournalJson
{
    public static string Serialize(JournalRun run)
    {
        if (run == null)
            throw new ArgumentNullException(nameof(run));

        var serializer = new DataContractJsonSerializer(typeof(JournalRun));
        using var stream = new MemoryStream();
        using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true))
        {
            serializer.WriteObject(writer, run);
            writer.Flush();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static JournalRun Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Journal JSON is empty.");

        var serializer = new DataContractJsonSerializer(typeof(JournalRun));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (JournalRun?)serializer.ReadObject(stream)
            ?? throw new InvalidDataException("Journal JSON did not contain a run.");
    }
}
