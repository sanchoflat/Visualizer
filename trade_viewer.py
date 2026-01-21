import pandas as pd
import plotly.graph_objects as go
import plotly.io as pio
from plotly.subplots import make_subplots
import os
from datetime import datetime, timedelta, time

# --- НАСТРОЙКИ ОТРИСОВКИ ---
# Принудительно открываем в браузере, чтобы не зависало в PyCharm SciView
pio.renderers.default = "browser"

# --- КОНФИГУРАЦИЯ ---
FILE_PATH = r"C:\Users\GeT\RiderProjects\honeybadger.arbitrage\HoneyBadger.Arbitrage\HoneyBadger.Arbitrage\bin\Debug\net9.0\Restored\Cache_BERAUSDT_0.csv"

# Цвета
COLORS = {
    'spot_ask': 'rgba(178, 34, 34, 0.5)',
    'spot_bid': 'rgba(34, 139, 34, 0.5)',
    'perp_ask': '#FF0000',
    'perp_bid': '#00AA00',
    'my_buy': '#008000',
    'my_sell': '#CC0000',
    'border_0': '#8B4500',
    'border_1': '#FF8C00',
    'border_2': '#008080',
    'border_3': '#00008B',
    'spread_0': '#1f77b4',
    'spread_1': '#9467bd',
}


def parse_logs(filepath):
    data = {
        'spot_prices': [],
        'linear_prices': [],
        'completed_orders': [],
        'trades': [],
        'spreads': [],
        'borders': []
    }

    active_slots = {}
    active_orders_by_id = {}

    # Состояние времени
    time_state = {
        'base_date': datetime(2024, 1, 1),
        'offset': timedelta(0),
        'last_raw_dt': None,
        'last_adjusted_dt': None
    }

    if not os.path.exists(filepath):
        print(f"Файл {filepath} не найден.")
        return None

    print(f"Чтение файла {filepath}...")
    try:
        with open(filepath, 'r') as f:
            all_lines = f.readlines()
    except Exception as e:
        print(f"Ошибка чтения файла: {e}")
        return None

    print(f"Обработка {len(all_lines)} строк...")

    for line in all_lines:
        if '|' not in line: continue
        parts = line.split('|')
        if len(parts) < 2: continue

        # --- 1. Парсинг времени ---
        raw_ts = parts[0].strip()
        try:
            if '.' in raw_ts:
                hms, frac = raw_ts.split('.')
                h, m, s = map(int, hms.split(':'))
                us = int(frac[:6].ljust(6, '0'))
            else:
                h, m, s = map(int, raw_ts.split(':'))
                us = 0

            t = time(h, m, s, us)
            current_raw_dt = datetime.combine(time_state['base_date'], t)

            if time_state['last_raw_dt']:
                delta = current_raw_dt - time_state['last_raw_dt']
                delta_sec = delta.total_seconds()

                if delta_sec < -36000:
                    if -46800 < delta_sec < -39600:  # -11..-12h
                        time_state['offset'] += timedelta(hours=12)
                    elif delta_sec < -80000:  # -23..-24h
                        time_state['offset'] += timedelta(hours=24)

            time_state['last_raw_dt'] = current_raw_dt
            timestamp = current_raw_dt + time_state['offset']
            time_state['last_adjusted_dt'] = timestamp

        except Exception:
            continue

        event_type = parts[1].strip()

        try:
            if event_type == 'Candle': continue

            # --- Top (Цены) ---
            if event_type == 'Top':
                if len(parts) < 5: continue
                symbol = parts[2].strip()
                try:
                    ask = float(parts[3])
                    bid = float(parts[4])
                    record = {'time': timestamp, 'ask': ask, 'bid': bid}
                    if 'Spot' in symbol:
                        data['spot_prices'].append(record)
                    elif 'Linear' in symbol:
                        data['linear_prices'].append(record)
                except:
                    pass

            # --- UserOrder ---
            elif event_type == 'UserOrder':
                if len(parts) < 9: continue
                symbol = parts[2].strip()
                try:
                    price = float(parts[3])
                except:
                    price = 0.0

                raw_side = parts[6].strip()
                side = raw_side.capitalize() if raw_side.lower() in ['buy', 'sell'] else None
                order_id = parts[7].strip()
                status = parts[8].strip()

                if status == 'New':
                    if not side: continue
                    slot_key = (symbol, side)
                    if slot_key in active_slots:
                        prev = active_slots[slot_key]
                        prev['end_time'] = timestamp
                        prev['final_status'] = 'Replaced'
                        data['completed_orders'].append(prev)
                        if prev['order_id'] in active_orders_by_id:
                            del active_orders_by_id[prev['order_id']]

                    new_order = {
                        'start_time': timestamp, 'price': price, 'side': side,
                        'symbol': symbol, 'order_id': order_id, 'status': status
                    }
                    active_slots[slot_key] = new_order
                    if order_id and order_id != '0':
                        active_orders_by_id[order_id] = new_order

                elif status in ['PartiallyFilled', 'Untriggered', 'Triggered']:
                    if order_id in active_orders_by_id:
                        active_orders_by_id[order_id]['status'] = status

                elif status in ['Filled', 'Cancelled', 'Canceled', 'Rejected']:
                    target = None
                    if order_id and order_id in active_orders_by_id:
                        target = active_orders_by_id[order_id]
                    elif side:
                        slot_key = (symbol, side)
                        if slot_key in active_slots:
                            target = active_slots[slot_key]

                    if target:
                        target['end_time'] = timestamp
                        target['final_status'] = status
                        data['completed_orders'].append(target)
                        if target['order_id'] in active_orders_by_id:
                            del active_orders_by_id[target['order_id']]
                        s_key = (target['symbol'], target['side'])
                        if s_key in active_slots and active_slots[s_key] == target:
                            del active_slots[s_key]

            # --- UserTrade ---
            elif event_type == 'UserTrade':
                if len(parts) < 6: continue
                symbol = parts[2].strip()
                try:
                    price = float(parts[3])
                except:
                    continue
                raw_side = parts[5].strip()
                side = raw_side.capitalize() if raw_side.lower() in ['buy', 'sell'] else None

                if side:
                    data['trades'].append({'time': timestamp, 'price': price, 'side': side, 'symbol': symbol})

            # --- Border ---
            elif event_type == 'Border':
                if len(parts) < 6: continue
                data['borders'].append({
                    'time': timestamp,
                    'b1': float(parts[2]), 'b2': float(parts[3]),
                    'b3': float(parts[4]), 'b4': float(parts[5])
                })

            # --- Spreads ---
            elif event_type == 'Spreads':
                if len(parts) < 3: continue
                s1 = float(parts[2])
                s2 = float(parts[3]) if len(parts) > 3 else s1
                data['spreads'].append({'time': timestamp, 's1': s1, 's2': s2})

        except Exception:
            continue

    # Закрываем хвосты
    end_time = time_state['last_adjusted_dt'] if time_state['last_adjusted_dt'] else timestamp
    for key, order in active_slots.items():
        order['end_time'] = end_time
        order['final_status'] = 'ActiveAtEnd'
        data['completed_orders'].append(order)

    # --- Создание DF ---
    dfs = {}

    def create_df(key):
        if data[key]:
            df = pd.DataFrame(data[key])
            df.sort_values('time', inplace=True)
            return df
        return pd.DataFrame()

    dfs['spot_prices'] = create_df('spot_prices')
    dfs['linear_prices'] = create_df('linear_prices')
    dfs['trades'] = create_df('trades')
    dfs['spreads'] = create_df('spreads')
    dfs['borders'] = create_df('borders')

    if data['completed_orders']:
        dfs['orders'] = pd.DataFrame(data['completed_orders'])
        dfs['orders'].sort_values('start_time', inplace=True)
    else:
        dfs['orders'] = pd.DataFrame()

    return dfs


def plot_crypto_data(dfs):
    fig = make_subplots(
        rows=2, cols=1,
        shared_xaxes=True,
        vertical_spacing=0.05,
        row_heights=[0.7, 0.3],
        subplot_titles=("Prices & Orders", "Spreads & Borders")
    )

    # 1. Prices
    if not dfs['spot_prices'].empty:
        fig.add_trace(
            go.Scattergl(x=dfs['spot_prices']['time'], y=dfs['spot_prices']['bid'], mode='lines', name='Spot Bid',
                         line=dict(color=COLORS['spot_bid'], width=1, dash='dot')), 1, 1)
        fig.add_trace(
            go.Scattergl(x=dfs['spot_prices']['time'], y=dfs['spot_prices']['ask'], mode='lines', name='Spot Ask',
                         line=dict(color=COLORS['spot_ask'], width=1, dash='dot')), 1, 1)
    if not dfs['linear_prices'].empty:
        fig.add_trace(go.Scattergl(x=dfs['linear_prices']['time'], y=dfs['linear_prices']['bid'], mode='lines',
                                   name='Futures Bid', line=dict(color=COLORS['perp_bid'], width=1)), 1, 1)
        fig.add_trace(go.Scattergl(x=dfs['linear_prices']['time'], y=dfs['linear_prices']['ask'], mode='lines',
                                   name='Futures Ask', line=dict(color=COLORS['perp_ask'], width=1)), 1, 1)

    # 2. Orders (Visible = Legend Only)
    if not dfs['orders'].empty:
        buy_x, buy_y = [], []
        sell_x, sell_y = [], []

        for _, order in dfs['orders'].iterrows():
            x = [order['start_time'], order['end_time'], None]
            y = [order['price'], order['price'], None]
            if order['side'] == 'Buy':
                buy_x.extend(x)
                buy_y.extend(y)
            else:
                sell_x.extend(x)
                sell_y.extend(y)

        if buy_x:
            fig.add_trace(go.Scatter(
                x=buy_x, y=buy_y, mode='lines+markers', name='My Buy',
                line=dict(color=COLORS['my_buy'], width=3),
                marker=dict(symbol='circle', size=5, color=COLORS['my_buy']),
                hoverinfo='all',
                visible='legendonly'
            ), 1, 1)
        if sell_x:
            fig.add_trace(go.Scatter(
                x=sell_x, y=sell_y, mode='lines+markers', name='My Sell',
                line=dict(color=COLORS['my_sell'], width=3),
                marker=dict(symbol='circle', size=5, color=COLORS['my_sell']),
                hoverinfo='all',
                visible='legendonly'
            ), 1, 1)

    # 3. Trades
    if not dfs['trades'].empty:
        buys = dfs['trades'][dfs['trades']['side'] == 'Buy']
        sells = dfs['trades'][dfs['trades']['side'] == 'Sell']

        if not buys.empty:
            fig.add_trace(go.Scatter(x=buys['time'], y=buys['price'], mode='markers', name='Trade Buy',
                                     marker=dict(symbol='triangle-up', size=12, color='#32CD32',
                                                 line=dict(width=1, color='black'))), 1, 1)
        if not sells.empty:
            fig.add_trace(go.Scatter(x=sells['time'], y=sells['price'], mode='markers', name='Trade Sell',
                                     marker=dict(symbol='triangle-down', size=12, color='#FF4500',
                                                 line=dict(width=1, color='black'))), 1, 1)

    # 4. Spreads & Borders
    if not dfs['borders'].empty:
        fig.add_trace(go.Scattergl(x=dfs['borders']['time'], y=dfs['borders']['b1'], mode='lines', name='B0',
                                   line=dict(color=COLORS['border_0'], width=1.5)), 2, 1)
        fig.add_trace(go.Scattergl(x=dfs['borders']['time'], y=dfs['borders']['b2'], mode='lines', name='B1',
                                   line=dict(color=COLORS['border_1'], width=1.5)), 2, 1)
        fig.add_trace(go.Scattergl(x=dfs['borders']['time'], y=dfs['borders']['b3'], mode='lines', name='B2',
                                   line=dict(color=COLORS['border_2'], width=1.5)), 2, 1)
        fig.add_trace(go.Scattergl(x=dfs['borders']['time'], y=dfs['borders']['b4'], mode='lines', name='B3',
                                   line=dict(color=COLORS['border_3'], width=1.5)), 2, 1)

    if not dfs['spreads'].empty:
        fig.add_trace(go.Scattergl(x=dfs['spreads']['time'], y=dfs['spreads']['s1'], mode='lines', name='S0',
                                   line=dict(color=COLORS['spread_0'], width=1.5)), 2, 1)
        fig.add_trace(go.Scattergl(x=dfs['spreads']['time'], y=dfs['spreads']['s2'], mode='lines', name='S1',
                                   line=dict(color=COLORS['spread_1'], width=1.5)), 2, 1)

    fig.update_layout(
        template='plotly_white',
        title=f'Trading Analysis: {len(dfs["orders"])} orders',
        height=900,
        hovermode='x unified',
        xaxis_rangeslider_visible=False
    )

    fig.update_yaxes(title_text="Price", tickformat='.6f', row=1, col=1)
    fig.update_yaxes(title_text="Spread", tickformat='.6f', row=2, col=1)

    # Явное указание рендерера
    fig.show(renderer="browser")


if __name__ == "__main__":
    print("Запуск анализатора...")
    dfs = parse_logs(FILE_PATH)
    if dfs:
        buys = len(dfs['orders'][dfs['orders']['side'] == 'Buy']) if not dfs['orders'].empty else 0
        sells = len(dfs['orders'][dfs['orders']['side'] == 'Sell']) if not dfs['orders'].empty else 0

        print(f"Найдено ордеров: {len(dfs['orders'])} (Buy: {buys}, Sell: {sells})")
        print(f"Найдено сделок: {len(dfs['trades'])}")
        plot_crypto_data(dfs)
    else:
        print("Данные не найдены.")
