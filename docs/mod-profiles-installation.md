# TCG Mod Profiles 0.3.0

Plugin indépendant pour les profils de mods par boutique. Save Manager 1.0.0 est requis : Mod Profiles utilise son Core (TCG.SaveManager.Core.dll).

## Installation

Jeu fermé, extraire le dossier BepInEx du paquet dans le dossier du jeu. Il ajoute ou remplace uniquement :

- BepInEx/plugins/TCGModProfiles/TCG.ModProfiles.dll
- BepInEx/patchers/TCG.ModProfiles.Preloader.dll

Si une ancienne TCG.ModProfiles.dll se trouve dans TCGSaveManagerDiagnostics ou ailleurs dans plugins, retirer uniquement cette ancienne copie pour éviter un doublon. Ne pas retirer les DLL Save Manager. Le fichier SHA256.txt permet de vérifier les deux fichiers distribués.

Le préchargeur utilise par défaut %LOCALAPPDATA%/TCGSM. Les lanceurs de laboratoire écrivent TCGModProfiles.root.txt pour leur espace isolé. Ne pas copier ce fichier du laboratoire dans Steam.

## Utilisation

Dans Mes boutiques, ouvrir Mods. Les choix Actif/Désactivé sont enregistrés par boutique. La liste est rangée en groupes repliables (Mods, Bibliothèques, Packs par dossier, Indispensables, Mods absents) avec une recherche et des actions par groupe. Un nouveau plugin suit le réglage « Nouveaux : à décider / actifs / désactivés », commun à toutes les boutiques (`campaigns/mod-settings.xml`). Une DLL mise à jour garde son choix et apparaît dans « À revoir ». « Copier depuis… » reprend les choix d'une autre boutique. Les mods absents restent visibles avec leur nom et leur ancien choix peut être désactivé.

Si les mêmes plugins sont déjà chargés, le changement de boutique est immédiat. Sinon, Appliquer et relancer enregistre le profil, ferme le jeu puis le rouvre et reprend le chargement. Depuis une demande de chargement, la sauvegarde précise est conservée. Depuis Mods seul, la reprise utilise la dernière auto, sinon la dernière manuelle, sinon une nouvelle partie.

Les dépendances obligatoires sont activées avec le mod choisi ; désactiver un mod propose de désactiver aussi ceux qui en dépendent. Incompatibilités et versions minimales sont contrôlées. Un mod nécessaire aux données de la sauvegarde reste requis. Le profil ne convertit pas les données d'un mod retiré.

## Fonctionnement et limites

Le préchargeur filtre les plugins en mémoire sans déplacer ni modifier leurs DLL. Un mod désactivé reste désactivé après sa mise à jour ; une DLL qui gagne un plugin non décidé reste chargée pour que la décision soit demandée au menu. Les fichiers de profils et de sélection sont séparés des sauvegardes natives. Windows PowerShell assure le relancement ; il attend au maximum 60 secondes la fermeture du processus précédent. La demande de reprise expire après 10 minutes et est consommée avant chargement.

Le parcours de relancement a été confirmé par le joueur. Les tests automatisés couvrent la sélection A/B/A, les variables Doorstop, les mods retirés/réajoutés et les messages. Les interfaces récentes restent soumises aux essais en jeu. Aucune installation automatique dans Steam n'est effectuée par les outils de ce projet.
