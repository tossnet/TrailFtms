# TrailFtms

[English](README.md) | **Français**

[![Build and deploy](https://github.com/tossnet/TrailFtms/actions/workflows/deploy.yml/badge.svg)](https://github.com/tossnet/TrailFtms/actions/workflows/deploy.yml)
[![Licence : MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Pilotez votre tapis de course FTMS au rythme d'une vidéo YouTube. Blazor WebAssembly + Web Bluetooth.

**Ouvrir l'application : <https://tossnet.github.io/TrailFtms/>**

![Écran Ride : la vidéo pilote le tapis, profil d'inclinaison coloré selon la vitesse](docs/images/ride.jpg)

Chaque vidéo YouTube est associée à un **programme** : la vitesse et l'inclinaison à appliquer à chaque
moment de la vidéo. Pendant la lecture, TrailFtms envoie ces consignes à votre tapis en Bluetooth :
le tapis monte quand le sentier monte. 100 % dans le navigateur : pas de compte, pas de serveur applicatif,
rien à installer.

> [!WARNING]
> **La sécurité d'abord.** TrailFtms pilote une machine automatiquement.
> - **Attachez toujours la clé de sécurité du tapis à vos vêtements** : c'est le seul arrêt qui
>   fonctionne même si l'application ou le Bluetooth ne répond plus.
> - Le bouton **STOP** (ou les touches **Espace** et **Échap**) arrête le tapis et la vidéo à tout moment.
> - Réglez une vitesse et une inclinaison maximales adaptées à votre niveau dans les réglages.
> - Le logiciel est fourni « tel quel », sans aucune garantie (voir la [licence](LICENSE)).
>   Vous l'utilisez sous votre propre responsabilité.

## Démarrage

1. Ouvrez l'application dans **Chrome ou Edge** (voir les [navigateurs compatibles](#navigateurs-compatibles)).
2. Allumez le tapis, attachez la clé de sécurité, et vérifiez qu'aucun autre appareil (application
   sur téléphone, autre onglet) n'y est connecté : le tapis n'accepte qu'une connexion Bluetooth.
3. Sur l'accueil, **Connecter le tapis** puis choisissez-le dans la liste du navigateur
   (`FS-xxxxxx` pour les modules FitShow). Les plages de vitesse et d'inclinaison lues sur le tapis s'affichent.
4. **Programmes**, puis **Démarrer** sur un programme, puis lancez la vidéo. Le tapis démarre quand la
   vidéo joue vraiment (pas pendant le chargement initial ni pendant une publicité).

## Fonctionnalités

- **La vidéo pilote le tapis.** L'application lit le temps de la vidéo 4 fois par seconde et n'envoie
  une commande que lorsque la consigne change. Tout est calé sur le temps de la vidéo, jamais sur une
  horloge : publicités, chargements et sauts dans la vidéo ne décalent pas le tapis.
- **Écran Ride** pensé pour une tablette posée sur le tapis : paysage ou portrait, vitesse et
  inclinaison réelles avec leurs consignes, prochain changement avec compte à rebours, profil
  d'inclinaison coloré selon la vitesse qui défile avec la vidéo sur les longs parcours (touchez un
  segment pour y aller), plein écran.
- **Sécurité** : bouton STOP toujours visible et raccourci clavier, synchro arrêtée en cas de perte de
  connexion, de retrait de la clé de sécurité ou d'arrêt depuis la console (rien ne redémarre sans
  vous), plafonds appliqués à toutes les commandes, avertissement au premier lancement.
- **Vidéo en pause** : continuer, ralentir à une vitesse de marche (par défaut), ou mettre le tapis en pause.
- **Fin de la vidéo** : retour progressif de l'inclinaison à 0, puis de la vitesse au minimum, puis arrêt.
- **Éditeur de programmes** : lancez la vidéo et appuyez sur **Ajouter un point ici** à chaque
  changement de pente ou d'allure. Enregistrement dans le navigateur, export JSON, partage.
- **Pilotage manuel** depuis l'accueil : vitesse, inclinaison, démarrer, pause, stop.
- Style Windows 10 classique (mode tactile) avec [Blazor-WinOld](https://github.com/tossnet/Blazor-WinOld),
  thèmes clair et sombre sur l'écran Ride.

| Programmes | Éditeur |
|---|---|
| ![Page Programmes](docs/images/programs.png) | ![Éditeur de programmes](docs/images/editor.jpg) |

## Navigateurs compatibles

TrailFtms s'appuie sur l'[API Web Bluetooth](https://developer.mozilla.org/fr/docs/Web/API/Web_Bluetooth_API).

| Plateforme | Chrome / Edge | Firefox | Safari |
|---|---|---|---|
| Windows, macOS, ChromeOS | Oui | Non | Non |
| Android | Oui | Non | |
| Linux | Expérimental (peut nécessiter `chrome://flags`) | Non | |
| iPhone / iPad | Non (tous les navigateurs iOS utilisent WebKit, sans Web Bluetooth) | Non | Non |

L'application affiche un message clair si le navigateur n'a pas Web Bluetooth. Si le navigateur bloque
les données de site, l'application fonctionne mais oublie les réglages et les programmes importés au
rechargement.

## Tapis testés

| Tapis | Module Bluetooth | Vitesse | Inclinaison | État |
|---|---|---|---|---|
| CARE Run-18 | FitShow (`FS-xxxxxx`) | 1,0 à 18,0 km/h, pas de 0,1 | 0 à 15 %, pas de 1 | Fonctionne |

À savoir sur le CARE Run-18 :
- le tapis bipe à chaque commande reçue (comportement du firmware, impossible à couper depuis
  l'application). Un programme avec moins de points fait moins de bips ;
- il répartit ses mesures sur plusieurs notifications Bluetooth, que l'application regroupe.

Tout tapis qui implémente le **Fitness Machine Service** Bluetooth (FTMS) avec des consignes de
vitesse et d'inclinaison devrait fonctionner. Vous avez essayé le vôtre ?
[Ouvrez une issue](https://github.com/tossnet/TrailFtms/issues) avec le modèle, le nom du module
Bluetooth et les plages affichées sur l'accueil, que ça marche ou non.

## Programmes

Un programme est un petit fichier JSON :

```json
{
  "id": "blue-pools-nz",
  "title": "Blue Pools, Nouvelle-Zélande",
  "youtubeId": "KiEN4HxM4ic",
  "author": "tossnet",
  "durationSeconds": 3112,
  "segments": [
    { "t": 0,  "speed": 1, "incline": 0 },
    { "t": 40, "speed": 3, "incline": 3 },
    { "t": 50, "speed": 3, "incline": 1 }
  ]
}
```

- `t` est le temps de la vidéo en secondes. Chaque segment s'applique jusqu'au suivant.
- `speed` en km/h, `incline` en %. Avant l'envoi, ils sont multipliés par l'allure réglée, limités par
  vos plafonds puis par les plages du tapis, et arrondis au pas du tapis.
- Le premier segment commence à `0` ; les temps sont uniques et avant la fin de la vidéo.

### Proposer un programme

1. Dans l'application : **Programmes > Nouveau programme**, collez le lien YouTube, lancez la vidéo et
   ajoutez un point à chaque changement de pente ou d'allure. **Enregistrer**, puis **Exporter**.
2. Ajoutez le fichier dans [`src/TrailFtms/wwwroot/data/programs/`](src/TrailFtms/wwwroot/data/programs/). Le nom
   du fichier est l'`id` du programme (minuscules, chiffres et tirets), par exemple `mon-sentier.json`.
3. Ajoutez ce nom de fichier dans [`index.json`](src/TrailFtms/wwwroot/data/programs/index.json), dans le même dossier.
4. Ouvrez une pull request. La CI vérifie que tous les programmes fournis sont valides.

Seules les vidéos dont le propriétaire autorise la lecture intégrée fonctionnent (sinon l'application le signale).

## Développement

Prérequis : [SDK .NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/TrailFtms   # puis ouvrez l'adresse affichée, dans Chrome ou Edge
dotnet test                          # MSTest + bUnit
```

| Dossier | Contenu |
|---|---|
| `src/TrailFtms/Services/Bluetooth` | Protocole FTMS (C# pur, entièrement testé) et service du tapis |
| `src/TrailFtms/Services/Ride` | `RideEngine` : du temps de la vidéo aux consignes du tapis |
| `src/TrailFtms/Services/Video` | Lecteur YouTube IFrame |
| `src/TrailFtms/Services/Programs` | Modèle de programme, validation, stockage, brouillon de l'éditeur |
| `src/TrailFtms/wwwroot/js` | Interop JavaScript minimale (Web Bluetooth, YouTube, stockage) |
| `tests/TrailFtms.Tests` | Tests unitaires et de composants, avec un tapis simulé |

Chaque push sur `main` est testé, publié et déployé sur GitHub Pages par
[`deploy.yml`](.github/workflows/deploy.yml).

## Idées pour la suite

- Le tapis pilote la vidéo (la vitesse de lecture suit votre allure).
- Générer un programme depuis une trace GPX ou les données GPS d'une GoPro.
- Exporter la séance (FIT / TCX) vers Strava.
- Liste des tapis testés alimentée par la communauté.

## Remerciements

- [Blazor-WinOld](https://github.com/tossnet/Blazor-WinOld) pour l'interface.
- [Bootstrap Icons](https://icons.getbootstrap.com) (MIT) pour les quelques icônes utilisées.
- Le programme Blue Pools suit la vidéo
  [Virtual Run 4K - Blue Pools](https://www.youtube.com/watch?v=KiEN4HxM4ic) de Virtual Running
  Adventures, lue avec le lecteur YouTube officiel.

## Licence

[MIT](LICENSE) © 2026 Christophe Peugnet
