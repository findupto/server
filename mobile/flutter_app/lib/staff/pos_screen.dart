import 'package:flutter/material.dart';

class PosScreen extends StatefulWidget {
  const PosScreen({super.key});

  @override
  State<PosScreen> createState() => _PosScreenState();
}

class _PosScreenState extends State<PosScreen> {
  final _barcode = TextEditingController();
  final _search = TextEditingController();
  final _cart = <int, _CartLine>{};
  String _orderType = 'Dine In';
  String _payment = 'Cash';
  String _status = '';

  final _products = const [
    (1, 'Pizza', 850.0, '1001'),
    (2, 'Burger', 450.0, '1002'),
    (3, 'Fries', 250.0, '1003'),
    (4, 'Cold Drink', 120.0, '1004'),
  ];

  void _add(int id, String name, double price) {
    setState(() {
      final line = _cart[id];
      _cart[id] = _CartLine(id, name, price, (line?.quantity ?? 0) + 1);
    });
  }

  void _scan() {
    final code = _barcode.text.trim();
    if (code.isEmpty) return;
    final match = _products.where((p) => p.$4 == code).firstOrNull;
    if (match == null) {
      setState(() => _status = 'Barcode not found: $code');
    } else {
      _add(match.$1, match.$2, match.$3);
      _barcode.clear();
      setState(() => _status = '${match.$2} added');
    }
  }

  double get _subtotal => _cart.values.fold(0, (s, x) => s + x.price * x.quantity);
  double get _tax => _subtotal * 0.0;
  double get _total => _subtotal + _tax;

  void _completeSale() {
    if (_cart.isEmpty) return;
    setState(() {
      _cart.clear();
      _status = 'Sale queued successfully';
    });
  }

  @override
  void dispose() {
    _barcode.dispose();
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final query = _search.text.toLowerCase();
    final products = _products.where((p) => p.$2.toLowerCase().contains(query) || p.$4.contains(query));
    return Scaffold(
      appBar: AppBar(title: const Text('FindUpTo POS — Counter')),
      body: Row(children: [
        Expanded(flex: 3, child: Padding(padding: const EdgeInsets.all(16), child: Column(children: [
          TextField(controller: _search, onChanged: (_) => setState(() {}), decoration: const InputDecoration(prefixIcon: Icon(Icons.search), labelText: 'Search products', border: OutlineInputBorder())),
          const SizedBox(height: 12),
          TextField(controller: _barcode, onSubmitted: (_) => _scan(), decoration: const InputDecoration(prefixIcon: Icon(Icons.qr_code_scanner), labelText: 'Scan / enter barcode', border: OutlineInputBorder())),
          const SizedBox(height: 16),
          Expanded(child: GridView.count(crossAxisCount: 3, childAspectRatio: 1.35, crossAxisSpacing: 10, mainAxisSpacing: 10, children: [for (final p in products) Card(child: InkWell(onTap: () => _add(p.$1, p.$2, p.$3), child: Center(child: Column(mainAxisSize: MainAxisSize.min, children: [Text(p.$2, style: Theme.of(context).textTheme.titleMedium), const SizedBox(height: 6), Text('Rs. ${p.$3.toStringAsFixed(0)}')]))))]))),
        ]))),
        const VerticalDivider(width: 1),
        Expanded(flex: 2, child: Padding(padding: const EdgeInsets.all(16), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text('Current Sale', style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 8),
          Expanded(child: _cart.isEmpty ? const Center(child: Text('Scan or select products')) : ListView(children: [for (final line in _cart.values) ListTile(title: Text(line.name), subtitle: Text('Rs. ${line.price.toStringAsFixed(0)} × ${line.quantity}'), trailing: Row(mainAxisSize: MainAxisSize.min, children: [IconButton(onPressed: () => setState(() { if (line.quantity > 1) { line.quantity--; } else { _cart.remove(line.id); } }), icon: const Icon(Icons.remove)), IconButton(onPressed: () => _add(line.id, line.name, line.price), icon: const Icon(Icons.add))]))])),
          DropdownButtonFormField<String>(value: _orderType, items: const ['Dine In', 'Takeaway', 'Delivery'].map((x) => DropdownMenuItem(value: x, child: Text(x))).toList(), onChanged: (x) => setState(() => _orderType = x!), decoration: const InputDecoration(labelText: 'Order type')),
          DropdownButtonFormField<String>(value: _payment, items: const ['Cash', 'Card', 'Online'].map((x) => DropdownMenuItem(value: x, child: Text(x))).toList(), onChanged: (x) => setState(() => _payment = x!), decoration: const InputDecoration(labelText: 'Payment')),
          const SizedBox(height: 12),
          Text('Subtotal: Rs. ${_subtotal.toStringAsFixed(2)}'),
          Text('Tax: Rs. ${_tax.toStringAsFixed(2)}'),
          Text('TOTAL: Rs. ${_total.toStringAsFixed(2)}', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 12),
          FilledButton.icon(onPressed: _completeSale, icon: const Icon(Icons.point_of_sale), label: const Text('Complete Sale')),
          if (_status.isNotEmpty) Padding(padding: const EdgeInsets.only(top: 8), child: Text(_status)),
        ]))),
      ]),
    );
  }
}

class _CartLine {
  _CartLine(this.id, this.name, this.price, this.quantity);
  final int id;
  final String name;
  final double price;
  int quantity;
}
