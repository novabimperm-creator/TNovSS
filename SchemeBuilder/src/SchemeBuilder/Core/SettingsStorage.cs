using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace SchemeBuilder.Core
{
    /// <summary>
    /// Хранение настроек в самой модели через ExtensibleStorage.
    ///
    /// Почему не параметр проекта и не файл рядом: параметр видно пользователю и его случайно
    /// затирают, файл теряется при передаче модели смежникам. Хранилище невидимо, переживает
    /// сохранение и уезжает вместе с RVT.
    /// </summary>
    internal static class SettingsStorage
    {
        private static readonly Guid SchemaGuid = new Guid("a047f71c-58e6-4365-a4fd-9da8be293c78");
        private const string SchemaName = "SchemeBuilderLegend";
        private const string FieldName = "Settings";

        public static SchemeSettings Read(Document document)
        {
            DataStorage storage = FindStorage(document);
            if (storage == null) return SchemeSettings.Default();

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return SchemeSettings.Default();

            try
            {
                Entity entity = storage.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return SchemeSettings.Default();

                return SchemeSettings.Deserialize(entity.Get<string>(schema.GetField(FieldName)));
            }
            catch (Exception)
            {
                // Схема из другой версии плагина — начинаем с чистых настроек, а не падаем.
                return SchemeSettings.Default();
            }
        }

        /// <summary>Вызывается внутри открытой транзакции.</summary>
        public static void Write(Document document, SchemeSettings settings)
        {
            Schema schema = Schema.Lookup(SchemaGuid) ?? CreateSchema();

            DataStorage storage = FindStorage(document);
            if (storage == null)
            {
                storage = DataStorage.Create(document);
                storage.Name = SchemaName;
            }

            var entity = new Entity(schema);
            entity.Set(schema.GetField(FieldName), settings.Serialize());
            storage.SetEntity(entity);
        }

        private static DataStorage FindStorage(Document document)
        {
            Schema schema = Schema.Lookup(SchemaGuid);

            return new FilteredElementCollector(document)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(s => schema == null
                    ? s.Name == SchemaName
                    : s.GetEntity(schema) != null && s.GetEntity(schema).IsValid());
        }

        private static Schema CreateSchema()
        {
            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetDocumentation("Настройки легенды УГО плагина «Структурная схема».");

            // Vendor-доступ: настройки правит только этот плагин, но читать может кто угодно.
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);

            builder.AddSimpleField(FieldName, typeof(string));

            return builder.Finish();
        }
    }
}
