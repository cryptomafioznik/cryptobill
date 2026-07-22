# Инструкция для Claude

Распакуй `cryptobill_trials25d_articulated_pack_v2` и прочитай `README_RU.md` и `asset-pack.json`.

Подключай пакет только после исправления physics acceptance-level. Не меняй физику ради совпадения с PNG.

1. Добавь режим `rigDebug=1`: точки frontAxle, rearAxle, steeringPivot, swingarmPivot, chassisPivot и helmetHitPoint.
2. Собери байк слоями в порядке из README.
3. Откалибруй joints один раз на `body_neutral`, затем используй одни координаты для всех поз.
4. Если поза не совпадает с joints больше чем на 3 px, не компенсируй это физикой: поправь только render offset этой позы.
5. Вращай колёса по пройденному расстоянию, а весь rig — по physicsAngle.
6. Ground weight shift выбирает body pose и меняет физический COM; не добавляет напрямую angular velocity.
7. В воздухе используй `body_air_tuck`, при жёстком контакте — `body_land_compress` на 80–140 мс.
8. Текстуру трассы маппить отдельно на каждый quad. На вершинах bevel, на разрывах отдельные caps.
9. Окружение: sky 0.03, far city 0.08, mid city 0.12, palms 0.28. Модули A/B чередовать.
10. Базовые объекты лежат в `objects/base_from_v1`, новые состояния — в `objects/`.

До художественной полировки покажи статический screenshot rigDebug и видео одной acceptance-трассы. Критерии: ступицы совпадают с axle points, нет биения колёс, вилка/маятник не отрываются, кромки трассы не пересекаются крестом, зона посадки видна заранее.

