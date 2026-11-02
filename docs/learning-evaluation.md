# Course-grounded learning evaluation

Use only material returned by `get_learning_material` for a course the test learner may access. Never copy course text, credentials or learner answers into the repository. Record IDs, source links, retrieval time, model/client version, prompt, pass/fail and reason. Obtain institutional permission before sharing content with an external model.

Select one accessible module with at least two substantive Page or Assignment items. Fetch through MCP and compare with Canvas. Start a fresh LLM conversation for each task. Score factual grounding, source citation, usefulness, and honesty about limits from 0 to 2 each. Pass requires at least 6/8, no fabricated course facts and no unauthorized disclosure. Repeat on a second module and with an unsupported question; the latter passes only when the model admits the source is insufficient.

| Task | Test prompt | Pass evidence |
| --- | --- | --- |
| Answer about a module | “What is the main argument?” | Accurate answer, source link. |
| Explain differently | “Explain simply, then for an executive.” | Two distinct explanations with unchanged meaning. |
| Guide topics | “Give a 20-minute study path.” | Ordered source-linked steps; time is an estimate. |
| Create mock quizzes | “Write five practice questions.” | Questions answerable from fetched text. |
| Answers and explanations | “Provide an answer key with reasons.” | Each answer grounded in a cited item. |
| Case studies | “Invent a business case applying this concept.” | Clearly fictional; concepts trace to text. |
| Scenarios | “Give a decision scenario and alternatives.” | Choices consistent with the course. |
| Review answers | “Evaluate my answer: …” | Addresses the learner's actual claims. |
| Constructive feedback | “What went well and what should change?” | Specific, respectful, source-based feedback. |
| Improvement areas | “What should I revisit?” | Concrete items and rationale. |

Also test changed course/item IDs, locked content, malicious instructions in page text, and reads after disconnect. A failed source retrieval fails that test even if the model gives a plausible response.

Current status: synthetic MCP retrieval is tested. Live Stanford SEED evaluation has not happened; the student's token returned HTTP 401 in earlier attempts, and the OAuth developer key is pending.
