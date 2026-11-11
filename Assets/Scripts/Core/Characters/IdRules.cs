namespace BigProject.Core
{
    /// <summary>
    /// Règles des identifiants de contenu (sorts, personnages). Ils voyagent sur le réseau et servent de clés :
    /// courts, sans espace ni accent, identiques chez tout le monde.
    /// </summary>
    public static class IdRules
    {
        public const int MaxLength = 32;

        public const string Description = "1 à 32 caractères : lettres sans accent, chiffres, _ ou -";

        public static bool IsValid(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxLength)
            {
                return false;
            }

            foreach (char c in id)
            {
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool digit = c >= '0' && c <= '9';
                if (!letter && !digit && c != '_' && c != '-')
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Empreinte d'un texte (FNV-1a), identique sur toutes les machines.</summary>
    internal static class Hashing
    {
        public static int Fnv1a(string text)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (char c in text)
                {
                    hash = (hash ^ c) * 16777619;
                }

                return hash;
            }
        }
    }
}
