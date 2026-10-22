# TCG Mod Profiles — première étape

Inventaire statique disponible dans mod-inventory-002.json : 18 plugins BepInEx, aucune DLL illisible après ajout des chemins de résolution. Chaque entrée contient identifiant, nom, version, fichier, type, empreinte SHA-256, dépendances obligatoires/facultatives, incompatibilités déclarées et filtres de processus. Aucun code de plugin n'a été exécuté et aucun fichier installé n'a été changé.

Enhanced Prefab Loader déclare une dépendance obligatoire à EnhancedPrefabLoader.API >= 6.1.0. Plusieurs intégrations Phone, ShopOS et Binder sont facultatives : ne pas les sélectionner automatiquement comme des dépendances obligatoires. L'inventaire repose sur les attributs déclarés, pas sur une preuve que le mod n'utilise aucune autre dépendance dynamique.

Le futur profil d'une boutique mémorisera une décision pour chaque plugin (actif, désactivé ou à décider), son identité/version examinée et ses dépendances. Save Manager et son préchargeur resteront indispensables. Plusieurs plugins dans une même DLL devront être traités ensemble si la sélection s'applique au fichier. Les bibliothèques sans attribut BepInPlugin ne devront pas être désactivées simplement parce qu'elles n'apparaissent pas comme un plugin.

Étape suivante : modèle de profil et validation hors jeu (dépendances, nouvelles versions, nouveaux mods, incompatibilités), puis interface de sélection dans le gestionnaire. L'application des profils au démarrage demandera un redémarrage ; aucun arrêt universel à chaud n'est promis. L'affichage filtré dans le téléphone viendra après l'application réelle.

Commande de lecture seule : mod-inventory <dossier BepInEx/plugins> <nouveau fichier JSON>, via l'outil Sandbox. mod-inventory-001.json conserve le premier essai incomplet pour diagnostic.
