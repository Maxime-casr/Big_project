using System.Text;

namespace BigProject.Client
{
    /// <summary>Identifiants de contenu déduits du nom d'un asset.</summary>
    public static class AssetIds
    {
        /// <summary>« Boule de Feu » devient « boule_de_feu » : minuscules, sans accent, sans espace.</summary>
        public static string Slug(string text)
        {
            var builder = new StringBuilder();
            foreach (char c in (text ?? string.Empty).ToLowerInvariant())
            {
                builder.Append(char.IsLetterOrDigit(c) && c < 128 ? c : '_');
            }

            return builder.ToString().Trim('_');
        }
    }
}
