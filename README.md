# 📚 Bio-Tek — Estrutura de Banco de Dados

Este documento descreve a modelagem de dados do sistema **Bio-Tek**, uma aplicação de biblioteca pessoal com recursos sociais, permitindo que usuários gerenciem livros, realizem avaliações e interajam com outros usuários.

---

# 🧠 Visão Geral

O sistema é composto pelas seguintes entidades principais:

* Usuário
* Livro
* Coleção
* Livro do Usuário
* Avaliação
* Comentário
* Seguidor

A estrutura foi pensada para:

* Escalabilidade
* Separação de responsabilidades
* Facilidade de integração com API REST
* Compatibilidade com bancos NoSQL (ex: MongoDB)

---

# 👤 Usuário

Representa um usuário autenticado via login social.

```text
Usuario
- id
- provedor (google | facebook)
- idProvedor
- nome
- email
- descricao
- tema (claro | escuro)
- ehAdministrador (boolean)
- ativo (boolean)
- dataCriacao
```

📌 Observações:

* Não armazena senha
* Dados vêm do provedor (Google/Facebook)
* Métricas (quantidade de livros, média etc.) são calculadas, não armazenadas

---

# 📚 Livro

Representa um livro global no sistema.

```text
Livro
- id
- titulo
- autor
- imagem
- codigoBarras (ISBN)
- idColecao (opcional)
- dataCriacao
```

📌 Observações:

* Pode ser reutilizado por vários usuários
* Pode pertencer a uma coleção

---

# 📦 Coleção

Representa séries de livros, mangás ou HQs.

```text
Colecao
- id
- nome
- descricao
- dataCriacao
```

📌 Exemplos:

* Naruto
* Berserk
* Senhor dos Anéis

---

# 📖 LivroUsuario

Relaciona um usuário com um livro.

```text
LivroUsuario
- id
- idUsuario
- idLivro
- status (queroLer | lendo | lido)
- dataCriacao
```

📌 Observações:

* Controla o progresso de leitura
* Um usuário pode ter vários livros

---

# ⭐ Avaliação

Representa a avaliação de um usuário sobre um livro.

```text
Avaliacao
- id
- idUsuario
- idLivro
- nota (0 a 5)
- comentario
- dataCriacao
```

📌 Regras:

* Só pode ser criada se o livro estiver marcado como **lido**
* Base para recomendações e feed social

---

# 💬 Comentário

Representa comentários em avaliações.

```text
Comentario
- id
- idUsuario
- idAvaliacao
- texto
- dataCriacao
```

📌 Observações:

* Comentários são feitos **sobre avaliações**, não diretamente sobre livros
* Garante maior qualidade nas interações

---

# 👥 Seguidor

Representa o relacionamento entre usuários.

```text
Seguidor
- id
- idSeguidor
- idSeguido
- dataCriacao
```

📌 Observações:

* Permite criar rede social
* Base para feed personalizado

---

# 🔗 Relacionamentos

```text
Usuario
 ├── LivroUsuario
 ├── Avaliacao
 ├── Comentario
 └── Seguidor

Livro
 ├── Colecao
 ├── LivroUsuario
 └── Avaliacao

Avaliacao
 └── Comentario
```

---

# 🔥 Regras de Negócio

## 📌 Avaliação

* Só pode avaliar livros com status **lido**

## 📌 Comentários

* Só podem ser feitos em avaliações

## 📌 Coleções

* Um livro pode ou não pertencer a uma coleção

## 📌 Feed (Comunidade)

Baseado em:

* Avaliações
* Usuários seguidos

---

# 🚀 Possíveis Evoluções Futuras

* Progresso de leitura (%)
* Marcar como favorito
* Sistema de recomendação inteligente
* Integração com APIs externas de livros
* Notificações entre usuários

---

# 💬 Conclusão

A estrutura atual permite:

* Gerenciamento completo de biblioteca pessoal
* Interação social entre usuários
* Escalabilidade para novas funcionalidades
* Integração eficiente com backend .NET e MongoDB

---
