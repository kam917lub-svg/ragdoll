# Vertical Impact Testsite — versione Unity

Serve Unity 6 (testato come codice per 6000.x) con **URP**.

## Come avviarlo
1. Unity Hub → **New project** → template **Universal 3D** → Create.
2. Copia la cartella `Assets/VITS` di questo repository dentro la cartella `Assets` del tuo progetto.
3. Apri una scena qualsiasi (va bene `SampleScene`) e premi **Play**.
   Il gioco costruisce da solo mappa, luci, giocatore e manichini
   (la camera e le luci del template vengono disattivate).
4. Clicca nella finestra Game per catturare il mouse.

## Comandi
WASD muoviti · Shift corri · Spazio salta · clic sinistro spara (semiautomatica, 15 colpi) ·
clic destro mira · R ricarica · `[` `]` velocità del tempo · P pausa · N nuovo manichino ·
T ricomincia · Esc libera il mouse.

## Cosa fa
- Manichini con ragdoll vero (Rigidbody + CharacterJoint): camminano, scappano quando spari,
  si tengono la ferita, cadono se colpiti alle gambe, svengono e muoiono dissanguati.
- Colpo in testa = morte istantanea; secondo colpo in testa = decapitazione.
- Arti: si staccano al 2° colpo (o 30% al 1°): il pezzo diventa un corpo fisico separato,
  il moncone spruzza sangue a getti pulsanti, volano pezzi di carne.
- Sangue: gocce balistiche, macchie tonde/allungate secondo velocità e angolo d'impatto,
  colature sui muri, macchie sui corpi, pozze che si allargano col volume.
- HUD: SPECIMENS, TIME, munizioni, monitor medico quando guardi un manichino.

## Novità
- Corpo a segmenti lisci come l'originale (bacino, torace, testa, braccia, avambracci+mani, cosce, stinchi+piedi),
  generati da codice; la carne si strappa dove escono i proiettili e sotto si vede l'interno.
- Gli arti si tagliano nel punto colpito (3° colpo, 2° al 50%, o troppa carne persa); i pezzi cadono con la fisica.
- Il sangue cola sulla pelle da una parte all'altra del corpo e gocciola a terra; pozze sotto i corpi.
- 6 muri COVER / 01-06: i Carl ci corrono dietro, o ci strisciano se colpiti alle gambe.
- Si possono colpire e tagliare anche i corpi a terra; i pezzi di carne non fermano i proiettili.

## Se qualcosa non va
Copia gli errori della **Console** e mandameli. Il codice non è stato compilato qui
(nell'ambiente cloud non c'è Unity).
Per una build standalone aggiungi `Universal Render Pipeline/Lit` e `Sprites/Default`
in Project Settings → Graphics → Always Included Shaders.
