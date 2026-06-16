# Prompt for Claude review

Скопируй текст ниже в Claude. Если есть возможность загрузить файлы проекта, загрузи:

- `docs/crypto-millionaire-game-plan.md`
- `docs/visual-direction.md`
- `docs/competitive-analysis-playbook.md`
- `index.html`
- `styles.css`
- `src/game.js`
- `assets/concepts/cryptobill-mobile-concept.svg`

```text
Ты выступаешь как независимый product/game design reviewer и market strategist.

Мы делаем игру CryptoBill: mobile-first/web-first crypto career tycoon / roguelite simulator. Идея: игрок начинает с маленьким виртуальным капиталом и проходит разные крипто-направления: spot trading, memecoins, DeFi, NFT, GameFi, futures/perps, staking/mining, security, builder/founder path, DAO/community. Цель игры - стать виртуальным crypto millionaire, но без реальных депозитов, выводов, обещаний заработка или обязательного Web3 на старте.

Наша текущая стратегия:
- делать game-first продукт, а не on-chain игру с первого дня;
- первый MVP как web/PWA, позже можно iPhone;
- вся экономика в MVP виртуальная;
- Solana/Web3 слой добавить позже и только опционально: achievements, badges, cosmetics, community, leaderboard;
- избегать token/NFT gating, real-money rewards, casino/gambling feel и App Store проблем;
- визуально игра должна быть premium strategy/tycoon dashboard, а не generic trading terminal и не crypto-gradient landing page.

Что уже сделано:
- концепт-документ игры;
- конкурентный playbook;
- визуальный direction frame;
- первый статический playable prototype: HTML/CSS/JS dashboard с portfolio chart, market events, actions Spot/Memes/DeFi/Research/Security/Sell, risk/stress/security/skill, next-week loop и концовками.

Пожалуйста, проанализируй проект максимально критично и практично.

Дай рекомендации по таким блокам:

1. Product positioning
- Насколько сильна сама идея?
- Как ее сформулировать, чтобы она была понятна игрокам за 5 секунд?
- Где есть риск стать слишком нишевой или слишком сложной игрой?

2. Game design
- Насколько правильный core loop?
- Какие решения игрока должны быть самыми интересными?
- Какие системы стоит добавить первыми?
- Какие системы лучше отложить?
- Как сделать проигрыш интересным, а не раздражающим?

3. MVP
- Что должно войти в первый реально тестируемый MVP?
- Что сейчас лишнее?
- Какой самый маленький vertical slice докажет, что игра имеет потенциал?

4. UX/UI
- Как улучшить первый экран?
- Что должно быть видно сразу?
- Где может быть перегруз информацией?
- Как сделать игру понятной новичку, но глубокой для crypto-native игрока?

5. Market and competitors
- С какими конкурентами или категориями нас нужно сравнивать?
- Чем CryptoBill может реально отличаться?
- Какие тренды mobile games / tycoon / trading simulators / GameFi стоит учитывать?

6. Monetization
- Какие модели монетизации лучше подходят?
- Какие модели опасны для доверия, App Store или retention?
- Как монетизировать без pay-to-win и без real-money earning promise?

7. iPhone and Web3/Solana
- Правильна ли стратегия сначала virtual game, потом optional Web3?
- Какие Web3-фичи могут реально усилить игру?
- Какие Web3-фичи лучше не делать?
- Какие App Store / compliance риски надо помнить?

8. Retention and growth
- Что заставит игрока возвращаться каждый день?
- Какие social/leaderboard/challenge механики можно добавить?
- Как сделать игру вирусной без дешевого referral/airdrop farming?

9. Risks
- Назови 10 главных рисков проекта.
- Для каждого предложи mitigation.

10. Concrete next steps
- Дай roadmap на ближайшие 2 недели.
- Дай roadmap на 6-8 недель.
- Дай список из 10 самых полезных задач для следующей итерации.

Формат ответа:
- Пиши структурированно.
- Будь честным и критичным.
- Не переписывай весь проект с нуля.
- Не предлагай собственный токен как обязательную часть.
- Не делай упор на real-money play-to-earn.
- Фокусируйся на шансах сделать качественную, популярную игру.
```
