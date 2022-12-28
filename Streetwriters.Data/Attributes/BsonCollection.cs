using System;

namespace Streetwriters.Data.Attributes
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class BsonCollectionAttribute : Attribute
    {
        public string CollectionName { get; }
        public string DatabaseName { get; }

        public BsonCollectionAttribute(string databaseName, string collectionName)
        {
            CollectionName = collectionName;
            DatabaseName = databaseName;
        }
    }
}