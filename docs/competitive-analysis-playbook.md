# Competitive analysis playbook

Дата: 2026-06-08

## Why this exists

CryptoBill входит сразу в несколько зрелых рынков:

- mobile tycoon/simulation games;
- trading simulators;
- crypto education apps;
- Web3/GameFi products;
- idle/rewarded crypto games;
- social/leaderboard challenge apps.

Значит, конкурировать нужно не "криптой", а качеством game loop, retention, понятностью, эмоциональными историями и скоростью улучшений.

## Standing rule

Перед каждой крупной фичей мы проверяем конкурентов и рынок:

- что уже есть;
- почему это работает;
- где игрокам скучно, больно или подозрительно;
- как сделать лучше без копирования;
- какие платформенные/юридические риски появились.

## Competitor buckets

### 1. Paper trading and education

Examples:

- CryptoNanny Academy
- InvestGame
- Crypto Trader Game
- SafeHaven-style trading simulators

Что смотреть:

- onboarding для новичков;
- paper trading UX;
- tournaments/leaderboards;
- chart interactions;
- educational content;
- trust language and disclaimers.

Как отличаться:

- не быть просто симулятором сделок;
- добавить career RPG, события, run structure, skill tree, risk consequences.

### 2. Crypto idle/rewarded games

Examples:

- Bitcoin Miner / Fumb Games;
- Crypto Miner Tycoon;
- Blockchain Tycoon-style games.

Что смотреть:

- session length;
- ad/IAP monetization;
- idle progression;
- withdrawal/reward complaints;
- simplicity of first minute.

Как отличаться:

- меньше "tap to earn";
- больше решений и историй;
- no real earning dependency;
- stronger premium game feel.

### 3. Web3/GameFi

Examples:

- Axie-like economies;
- Pixels / Ronin ecosystem;
- Off The Grid-style asset extraction;
- Sorare/Gods Unchained NFT economies;
- Solana gaming projects.

Что смотреть:

- on-chain depth vs fake activity;
- token sinks/sources;
- asset ownership;
- marketplace behavior;
- player retention after token hype;
- onboarding friction.

Как отличаться:

- blockchain optional, not mandatory;
- cosmetics/achievements first;
- no unsustainable earn loop.

### 4. Mainstream tycoon and strategy

Examples:

- Game Dev Tycoon;
- startup/business simulators;
- idle tycoon games;
- mobile hybridcasual strategy.

Что смотреть:

- one-more-turn pressure;
- upgrade pacing;
- readable complexity;
- failure states;
- narrative events;
- meta progression.

Как отличаться:

- crypto-specific risk map;
- market cycles;
- scams/exploits/liquidations as gameplay;
- multiple career paths.

## Feature review checklist

Перед добавлением фичи:

- Player fantasy: какую мечту/страх она усиливает?
- Decision depth: есть ли настоящий выбор?
- Risk: чем игрок платит за upside?
- Retention: захочет ли вернуться завтра?
- Learnability: новичок поймет без лекции?
- Monetization safety: нет ли pay-to-win или real-money risk?
- Platform safety: пройдет ли iOS без crypto/trading/licensing проблем?
- Differentiation: чем это лучше или свежее конкурентов?

## Metrics to track

На prototype/MVP:

- first-session completion;
- week-3/week-7/week-15 reach rate;
- action distribution;
- most common death reason;
- rage quit after loss;
- replay rate;
- average run length;
- D1/D7 retention after daily challenge;
- risk-adjusted leaderboard participation.

## Research cadence

Weekly:

- scan 3-5 direct competitors;
- check mobile game market notes;
- check App Store/Web3 policy changes if feature touches crypto;
- capture screenshots and note UX patterns;
- decide one improvement for CryptoBill.

Monthly:

- update competitor map;
- review monetization trends;
- review Web3 gaming data;
- choose one content expansion theme.

Before Solana/Web3 layer:

- re-check Apple App Review Guidelines;
- re-check Solana/Phantom mobile wallet support;
- legal review for rewards, contests, tokens, NFTs;
- test wallet UX on web and Android separately from iOS.

## Current strategic stance

Build as a high-quality virtual crypto career game first.

Add Web3 only when it clearly improves:

- identity;
- community;
- collectibles;
- leaderboard trust;
- social proof;
- creator ecosystem.

Do not add Web3 just because the subject is crypto.
