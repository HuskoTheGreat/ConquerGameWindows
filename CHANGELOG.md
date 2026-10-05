# Changelog

Every push to `main` is published as the latest build, and installed copies update themselves on their next start.
Notable changes are listed here, newest first.

## Unreleased

- A bright cartoon look: illustrated tiles (forests, brick pits, sheep pastures, wheat fields, mountains, dunes), a
  wavy sea, parchment panels, a card fan for your hand and a colour-coded game log that folds away.
- Resources are now Wood, Brick, Sheep, Wheat and Stone.
- Computer players have first names and a BOT tag.
- Fixed: during a computer player's turn the screen showed its hand; the discard screen let you pick too many
  cards; trades could offer and ask for the same resource; the board was squeezed on small windows.
- A proper title screen, the game icon on the window, a one-row action bar, a matching look for every dialog,
  hex highlighting under the mouse and a victory screen with the final standings.
- Escape closes dialogs.
- New title-page README with a download button, a rules guide ([How to play](docs/HOW_TO_PLAY.md)) and a developer
  guide ([DEVELOPMENT.md](docs/DEVELOPMENT.md)).
- Continuous integration runs every test on each push and pull request.
- Issue templates for bug reports and ideas.

## 1.0 (October 2026)

The first complete release of Conquer.

- **Single player** against up to five computer players (Easy, Normal, Hard), or pass and play on one screen with
  hidden hands.
- **Online play** through a dedicated server: room codes, chat, reconnecting to your seat, and private hands.
- **Procedural boards** from 7 to 127 tiles, freshly generated every game.
- **Full rules**: setup draft, production, the raider, discards, building, bank, harbor and player trades, five
  action cards, Great Road, Grand Army and hidden victory points.
- **House rules** that can change mid-game, plus optional wild cards in the action deck.
- **Animations**: tumbling dice, flying resource cards, action-card reveals, building pops, turn banners and a
  victory screen.
- **Windows installer** with a launcher that keeps the game up to date automatically.
