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
| Tasto | Azione |
|---|---|
| W A S D | muoversi |
| Shift | correre |
| Spazio | saltare |
| Ctrl sinistro (tenuto) | accovacciarsi (più lento; ti rialzi solo se c'è spazio) |
| Clic sinistro | sparare (pistola: un colpo per clic; AK-47: tieni premuto; AWP: un colpo, poi 1,2 s di otturatore) |
| Clic destro (tenuto) | mirare; con l'AWP = cannocchiale (girare la rotella = zoom 4x-12x) |
| Rotella (tenuta) | afferrare un Carl (vivo o morto, cervello ON o OFF) o un pezzo fino a 60 m e portarlo dove vuoi; girare la rotella = avvicina/allontana |
| R | ricaricare |
| T | raggi X (ossa, organi, arterie, vene, nervi) |
| Y | nuovo Carl nel punto indicato dal mirino (a terra, rivolto verso di te) |
| N | nuovo Carl in un punto casuale |
| [ ] | rallenta / accelera il tempo |
| P | pausa |
| Backspace | ricomincia (anche pulsante RESET nel menu Esc) |
| Esc | menu: scegli PISTOLA, AK-47 o AWP, SENSIBILITA mouse e SENSIBILITA ADS (default 0,7x, salvate), CARL BRAINS on/off (off = stanno fermi finché non vengono presi di mira), riprendi |

Sotto il mirino c'è scritto cosa stai puntando; una X rossa conferma che il colpo ha preso un corpo.

## Cosa fa
- Armi: pistola 9 mm (15), AK-47 7.62 (30; in testa grosso foro d uscita, niente esplosione), AWP .338 Lapua (10, otturatore, cannocchiale; stacca un arto in un colpo, esplode sempre la testa, attraversa un corpo e colpisce quello dietro).
- Il sangue non sparisce: ~24 000 macchie per tipo, 8 000 sui corpi, fino a 2 000 pozze (poi si allargano quelle esistenti).
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

- T = raggi X: pelle trasparente, si vedono ossa, organi, arterie (rosse), vene (blu), nervi (gialli).

## Se qualcosa non va
Copia gli errori della **Console** e mandameli. Il codice non è stato compilato qui
(nell'ambiente cloud non c'è Unity).
Per una build standalone aggiungi `Universal Render Pipeline/Lit` e `Sprites/Default`
in Project Settings → Graphics → Always Included Shaders.
